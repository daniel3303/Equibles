using Equibles.Errors.BusinessLogic;
using Equibles.CommonStocks.BusinessLogic;
using Equibles.CommonStocks.Data.Models;
using Equibles.Errors.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Integrations.Sec.Contracts;
using Equibles.Integrations.Sec.Models;
using Equibles.Core.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Equibles.Sec.HostedService.Services;

public class CompanySyncService : ICompanySyncService {
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly ISecEdgarClient _secEdgarClient;
    private readonly WorkerOptions _workerOptions;
    private readonly ILogger<CompanySyncService> _logger;
    private readonly ErrorReporter _errorReporter;

    public CompanySyncService(IServiceScopeFactory serviceScopeFactory,
        ISecEdgarClient secEdgarClient,
        IOptions<WorkerOptions> workerOptions,
        ILogger<CompanySyncService> logger,
        ErrorReporter errorReporter) {
        _serviceScopeFactory = serviceScopeFactory;
        _secEdgarClient = secEdgarClient;
        _workerOptions = workerOptions.Value;
        _logger = logger;
        _errorReporter = errorReporter;
    }

    public async Task SyncCompaniesFromSecApi() {
        try {
            _logger.LogInformation("Syncing companies from SEC Edgar API...");

            var secCompanies = await _secEdgarClient.GetActiveCompanies();
            _logger.LogInformation("Retrieved {CompanyCount} companies from SEC API", secCompanies.Count);

            // Filter by configured tickers if specified
            if (_workerOptions.TickersToSync?.Count > 0) {
                secCompanies = secCompanies
                    .Where(c => c.Tickers.Any(ticker => _workerOptions.TickersToSync.Contains(ticker)))
                    .ToList();
                _logger.LogInformation("Filtered to {CompanyCount} companies based on configured tickers",
                    secCompanies.Count);
            }

            using var scope = _serviceScopeFactory.CreateScope();
            var commonStockRepository = scope.ServiceProvider.GetRequiredService<CommonStockRepository>();
            var commonStockManager = scope.ServiceProvider.GetRequiredService<CommonStockManager>();
            var dbContext = scope.ServiceProvider.GetRequiredService<EquiblesDbContext>();

            // Get all CIKs from SEC companies
            var secCiks = secCompanies.Select(c => c.Cik).ToHashSet();

            // Get existing companies from database
            var existingStocks = await commonStockRepository.GetByCiks(secCiks).ToListAsync();
            var existingCiks = existingStocks.Select(cs => cs.Cik).ToHashSet();

            // Primary tickers are globally unique — collisions on primary mean the incoming
            // company must replace or skip. Secondary tickers may legitimately overlap across
            // related SEC filers (e.g. parent REIT + operating partnership sharing a
            // preferred-share ticker), so they are tracked separately and never drive
            // replace/skip routing decisions.
            var existingPrimaryTickers = (await commonStockRepository.GetAllTickers().ToListAsync())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var existingSecondaryTickers =
                (await commonStockRepository.GetAllSecondaryTickers().ToListAsync())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var state = new StockSyncState {
                SecCiks = secCiks,
                ExistingStocks = existingStocks,
                ExistingCiks = existingCiks,
                ExistingPrimaryTickers = existingPrimaryTickers,
                ExistingSecondaryTickers = existingSecondaryTickers,
                CommonStockRepository = commonStockRepository,
                CommonStockManager = commonStockManager,
                DbContext = dbContext
            };

            foreach (var secCompany in secCompanies) {
                var primaryTicker = secCompany.Tickers.FirstOrDefault();
                if (string.IsNullOrEmpty(primaryTicker)) {
                    _logger.LogWarning("Company {CompanyName} (CIK: {Cik}) has no tickers, skipping", secCompany.Name,
                        secCompany.Cik);
                    continue;
                }

                var secondaryTickers = secCompany.Tickers.Skip(1).ToList();

                if (state.ExistingCiks.Contains(secCompany.Cik)) {
                    await UpdateExistingStock(secCompany, primaryTicker, secondaryTickers, state);
                } else {
                    // Only a primary-ticker collision requires the replace/skip branch —
                    // overlap with another company's secondaries is allowed by the domain.
                    if (state.ExistingPrimaryTickers.Contains(primaryTicker))
                        await ReplaceObsoleteStock(secCompany, primaryTicker, secondaryTickers, state);
                    else
                        await CreateNewStock(secCompany, primaryTicker, secondaryTickers, state);
                }
            }

            _logger.LogInformation("Company synchronization completed successfully");
        } catch (Exception ex) {
            _logger.LogError(ex, "Error while syncing companies from SEC API");
            throw;
        }
    }

    private async Task UpdateExistingStock(CompanyInfo secCompany, string primaryTicker,
        List<string> secondaryTickers, StockSyncState state) {
        var existingStock = state.ExistingStocks.First(cs => cs.Cik == secCompany.Cik);
        var needsUpdate = existingStock.Ticker != primaryTicker
                          || existingStock.Name != secCompany.Name
                          || !(existingStock.SecondaryTickers ?? []).SequenceEqual(secondaryTickers);

        if (!needsUpdate)
            return;

        // Pre-check: only a collision against another company's primary ticker blocks us.
        // Secondary-ticker overlap is allowed by the domain.
        if (existingStock.Ticker != primaryTicker && state.ExistingPrimaryTickers.Contains(primaryTicker)) {
            var tickerHolder = state.ExistingStocks.FirstOrDefault(cs => cs.Ticker == primaryTicker);
            if (tickerHolder != null && !state.SecCiks.Contains(tickerHolder.Cik)) {
                // Old holder is no longer in SEC data - remove it
                try {
                    state.CommonStockRepository.Delete(tickerHolder);
                    await state.CommonStockRepository.SaveChanges();

                    state.ExistingCiks.Remove(tickerHolder.Cik);
                    state.ExistingPrimaryTickers.Remove(tickerHolder.Ticker);
                    foreach (var t in tickerHolder.SecondaryTickers) state.ExistingSecondaryTickers.Remove(t);
                    state.ExistingStocks.Remove(tickerHolder);

                    _logger.LogInformation(
                        "Removed obsolete company {Name} (CIK: {Cik}) holding ticker {Ticker}",
                        tickerHolder.Name, tickerHolder.Cik, primaryTicker);
                } catch (Exception ex) {
                    _logger.LogError(ex, "Error removing obsolete company for ticker {Ticker}", primaryTicker);
                    await _errorReporter.Report(ErrorSource.DocumentScraper,"CompanySync.RemoveObsolete", ex.Message, ex.StackTrace, $"ticker: {primaryTicker}");
                    return;
                }
            } else {
                _logger.LogWarning(
                    "Cannot update {OldTicker} to {NewTicker} (CIK: {Cik}) - ticker already in use by active company, skipping",
                    existingStock.Ticker, primaryTicker, secCompany.Cik);
                return;
            }
        }

        // Save old values for rollback
        var oldTicker = existingStock.Ticker;
        var oldName = existingStock.Name;
        var oldSecondaryTickers = existingStock.SecondaryTickers.ToList();

        try {
            existingStock.Ticker = primaryTicker;
            existingStock.Name = secCompany.Name;
            existingStock.SecondaryTickers = secondaryTickers;
            await state.CommonStockManager.Update(existingStock);

            // Update tracking sets on success
            if (oldTicker != primaryTicker) {
                state.ExistingPrimaryTickers.Remove(oldTicker);
                state.ExistingPrimaryTickers.Add(primaryTicker);
            }
            foreach (var t in oldSecondaryTickers) state.ExistingSecondaryTickers.Remove(t);
            foreach (var t in secondaryTickers) state.ExistingSecondaryTickers.Add(t);

            _logger.LogDebug("Updated company: {OldTicker} -> {NewTicker}, {OldName} -> {NewName}",
                oldTicker, primaryTicker, oldName, secCompany.Name);
        } catch (Exception ex) {
            // Revert entity to old values and detach changes to prevent dirty state
            existingStock.Ticker = oldTicker;
            existingStock.Name = oldName;
            existingStock.SecondaryTickers = oldSecondaryTickers;
            state.DbContext.Entry(existingStock).State = EntityState.Unchanged;
            _logger.LogError(ex, "Error updating company {Ticker} - {Name} (CIK: {Cik})",
                primaryTicker, secCompany.Name, secCompany.Cik);
            await _errorReporter.Report(ErrorSource.DocumentScraper,"CompanySync.UpdateStock", ex.Message, ex.StackTrace, $"ticker: {primaryTicker}, cik: {secCompany.Cik}");
        }
    }

    private async Task ReplaceObsoleteStock(CompanyInfo secCompany, string primaryTicker,
        List<string> secondaryTickers, StockSyncState state) {
        // Ticker exists - check if the current holder is still active in SEC data
        var obsoleteStock = state.ExistingStocks.FirstOrDefault(cs => cs.Ticker == primaryTicker);

        if (obsoleteStock == null || state.SecCiks.Contains(obsoleteStock.Cik)) {
            _logger.LogWarning(
                "Company {CompanyName} (CIK: {Cik}) has ticker {Ticker} already used by another active company, skipping",
                secCompany.Name, secCompany.Cik, primaryTicker);
            return;
        }

        // Old company no longer in SEC data - replace it
        try {
            state.CommonStockRepository.Delete(obsoleteStock);
            await state.CommonStockRepository.SaveChanges();

            state.ExistingCiks.Remove(obsoleteStock.Cik);
            state.ExistingPrimaryTickers.Remove(obsoleteStock.Ticker);
            foreach (var t in obsoleteStock.SecondaryTickers) state.ExistingSecondaryTickers.Remove(t);
            state.ExistingStocks.Remove(obsoleteStock);

            var newStock = await state.CommonStockManager.Create(new CommonStock {
                Ticker = primaryTicker,
                Name = secCompany.Name,
                Cik = secCompany.Cik,
                SecondaryTickers = secondaryTickers,
                Description = $"Company with tickers: {string.Join(", ", secCompany.Tickers)}",
                MarketCapitalization = 0,
                SharesOutStanding = 0
            });

            state.ExistingCiks.Add(secCompany.Cik);
            state.ExistingPrimaryTickers.Add(primaryTicker);
            foreach (var t in secondaryTickers) state.ExistingSecondaryTickers.Add(t);
            state.ExistingStocks.Add(newStock);

            _logger.LogInformation(
                "Replaced obsolete company {OldName} (CIK: {OldCik}) with {NewName} (CIK: {NewCik}) for ticker {Ticker}",
                obsoleteStock.Name, obsoleteStock.Cik, secCompany.Name, secCompany.Cik, primaryTicker);
        } catch (Exception ex) {
            _logger.LogError(ex, "Error replacing company for ticker {Ticker}", primaryTicker);
            await _errorReporter.Report(ErrorSource.DocumentScraper,"CompanySync.ReplaceStock", ex.Message, ex.StackTrace, $"ticker: {primaryTicker}, cik: {secCompany.Cik}");
        }
    }

    private async Task CreateNewStock(CompanyInfo secCompany, string primaryTicker,
        List<string> secondaryTickers, StockSyncState state) {
        CommonStock newStock = null;
        try {
            newStock = await state.CommonStockManager.Create(new CommonStock {
                Ticker = primaryTicker,
                Name = secCompany.Name,
                Cik = secCompany.Cik,
                SecondaryTickers = secondaryTickers,
                Description = $"Company with tickers: {string.Join(", ", secCompany.Tickers)}",
                MarketCapitalization = 0,
                SharesOutStanding = 0
            });

            // Add to tracking sets to avoid duplicates in this run
            state.ExistingCiks.Add(secCompany.Cik);
            state.ExistingPrimaryTickers.Add(primaryTicker);
            foreach (var t in secondaryTickers) state.ExistingSecondaryTickers.Add(t);
            state.ExistingStocks.Add(newStock);
            _logger.LogDebug("Created new company: {Ticker} - {Name} (CIK: {Cik})",
                primaryTicker, secCompany.Name, secCompany.Cik);
        } catch (Exception ex) {
            // Detach failed entity to prevent cascading DbContext errors
            if (newStock != null) {
                state.DbContext.Entry(newStock).State = EntityState.Detached;
            }
            _logger.LogError(ex, "Error creating company {Ticker} - {Name} (CIK: {Cik})",
                primaryTicker, secCompany.Name, secCompany.Cik);
            await _errorReporter.Report(ErrorSource.DocumentScraper,"CompanySync.CreateStock", ex.Message, ex.StackTrace, $"ticker: {primaryTicker}, cik: {secCompany.Cik}");
        }
    }

    private async Task<bool> IsOperatingCompany(CompanyInfo company) {
        if (company.EntityType != null)
            return company.IsOperatingCompany;

        var entityType = await _secEdgarClient.GetEntityType(company.Cik);
        company.EntityType = entityType;

        if (!company.IsOperatingCompany) {
            _logger.LogDebug("Skipping non-operating entity {Name} (CIK: {Cik}, type: {Type})",
                company.Name, company.Cik, entityType ?? "unknown");
        }

        return company.IsOperatingCompany;
    }

    private class StockSyncState {
        public HashSet<string> SecCiks { get; init; }
        public List<CommonStock> ExistingStocks { get; init; }
        public HashSet<string> ExistingCiks { get; init; }
        public HashSet<string> ExistingPrimaryTickers { get; init; }
        public HashSet<string> ExistingSecondaryTickers { get; init; }
        public CommonStockRepository CommonStockRepository { get; init; }
        public CommonStockManager CommonStockManager { get; init; }
        public DbContext DbContext { get; init; }
    }

}

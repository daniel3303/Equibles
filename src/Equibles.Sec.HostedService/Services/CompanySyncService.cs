using System.Globalization;
using Equibles.CommonStocks.BusinessLogic;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Core.Configuration;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Integrations.Sec.Contracts;
using Equibles.Integrations.Sec.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Equibles.Sec.HostedService.Services;

public class CompanySyncService : ICompanySyncService
{
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly ISecEdgarClient _secEdgarClient;
    private readonly WorkerOptions _workerOptions;
    private readonly ILogger<CompanySyncService> _logger;
    private readonly ErrorReporter _errorReporter;

    public CompanySyncService(
        IServiceScopeFactory serviceScopeFactory,
        ISecEdgarClient secEdgarClient,
        IOptions<WorkerOptions> workerOptions,
        ILogger<CompanySyncService> logger,
        ErrorReporter errorReporter
    )
    {
        _serviceScopeFactory = serviceScopeFactory;
        _secEdgarClient = secEdgarClient;
        _workerOptions = workerOptions.Value;
        _logger = logger;
        _errorReporter = errorReporter;
    }

    public async Task SyncCompaniesFromSecApi()
    {
        try
        {
            _logger.LogInformation("Syncing companies from SEC Edgar API...");

            var secCompanies = await _secEdgarClient.GetActiveCompanies();
            _logger.LogInformation(
                "Retrieved {CompanyCount} companies from SEC API",
                secCompanies.Count
            );

            if (_workerOptions.TickersToSync?.Count > 0)
            {
                secCompanies = secCompanies
                    .Where(c =>
                        c.Tickers.Any(ticker => _workerOptions.TickersToSync.Contains(ticker))
                    )
                    .ToList();
                _logger.LogInformation(
                    "Filtered to {CompanyCount} companies based on configured tickers",
                    secCompanies.Count
                );
            }

            using var scope = _serviceScopeFactory.CreateScope();
            var commonStockRepository =
                scope.ServiceProvider.GetRequiredService<CommonStockRepository>();
            var commonStockManager = scope.ServiceProvider.GetRequiredService<CommonStockManager>();
            var dbContext = scope.ServiceProvider.GetRequiredService<EquiblesDbContext>();

            var secCiks = secCompanies.Select(c => c.Cik).ToHashSet();

            // Load every existing stock so we can detect subsidiaries already attached
            // as SecondaryCiks on prior syncs. We can't filter by SEC CIKs alone because
            // the subsidiary's CIK won't match any incoming primary CIK — it lives only
            // inside another stock's SecondaryCiks list.
            var allExistingStocks = await commonStockRepository.GetAll().ToListAsync();
            var existingStocks = allExistingStocks.Where(cs => secCiks.Contains(cs.Cik)).ToList();
            var existingCiks = existingStocks.Select(cs => cs.Cik).ToHashSet();

            // Build the ticker → stock lookup over every row so ReplaceObsoleteStock can find
            // a ticker holder whose own CIK dropped out of SEC's feed but who still owns the
            // primary ticker our incoming company wants.
            var primaryTickerToStock = allExistingStocks.ToDictionary(s => s.Ticker, s => s);

            var secondaryCikToParent = BuildSecondaryCikToParent(allExistingStocks);

            // Primary tickers are globally unique — collisions on primary mean the incoming
            // company must replace or skip. Secondary tickers may legitimately overlap across
            // related SEC filers (e.g. parent REIT + operating partnership sharing a
            // preferred-share ticker), so they are tracked separately and never drive
            // replace/skip routing decisions.
            var existingPrimaryTickers = (
                await commonStockRepository.GetAllTickers().ToListAsync()
            ).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var existingSecondaryTickers = (
                await commonStockRepository.GetAllSecondaryTickers().ToListAsync()
            ).ToHashSet(StringComparer.OrdinalIgnoreCase);

            var state = new StockSyncState
            {
                SecCiks = secCiks,
                ExistingStocks = existingStocks,
                ExistingCiks = existingCiks,
                ExistingPrimaryTickers = existingPrimaryTickers,
                ExistingSecondaryTickers = existingSecondaryTickers,
                PrimaryTickerToStock = primaryTickerToStock,
                SecondaryCikToParent = secondaryCikToParent,
                CommonStockRepository = commonStockRepository,
                CommonStockManager = commonStockManager,
                DbContext = dbContext,
            };

            foreach (var secCompany in secCompanies)
            {
                if (state.SecondaryCikToParent.TryGetValue(secCompany.Cik, out var parent))
                {
                    _logger.LogDebug(
                        "Skipping subsidiary CIK {Cik} ({Name}) — already attached to parent {ParentTicker} (CIK: {ParentCik})",
                        secCompany.Cik,
                        secCompany.Name,
                        parent.Ticker,
                        parent.Cik
                    );
                    continue;
                }

                var primaryTicker = secCompany.Tickers.FirstOrDefault();
                if (string.IsNullOrEmpty(primaryTicker))
                {
                    _logger.LogWarning(
                        "Company {CompanyName} (CIK: {Cik}) has no tickers, skipping",
                        secCompany.Name,
                        secCompany.Cik
                    );
                    continue;
                }

                var secondaryTickers = secCompany.Tickers.Skip(1).ToList();

                if (state.ExistingCiks.Contains(secCompany.Cik))
                {
                    await UpdateExistingStock(secCompany, primaryTicker, secondaryTickers, state);
                }
                else
                {
                    // Only a primary-ticker collision requires the replace/skip branch —
                    // overlap with another company's secondaries is allowed by the domain.
                    if (state.ExistingPrimaryTickers.Contains(primaryTicker))
                        await ReplaceObsoleteStock(
                            secCompany,
                            primaryTicker,
                            secondaryTickers,
                            state
                        );
                    else
                        await CreateNewStock(secCompany, primaryTicker, secondaryTickers, state);
                }
            }

            _logger.LogInformation("Company synchronization completed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while syncing companies from SEC API");
            throw;
        }
    }

    private async Task UpdateExistingStock(
        CompanyInfo secCompany,
        string primaryTicker,
        List<string> secondaryTickers,
        StockSyncState state
    )
    {
        var existingStock = state.ExistingStocks.First(cs => cs.Cik == secCompany.Cik);
        var normalizedName = NormalizeCompanyName(secCompany.Name);
        var needsUpdate =
            existingStock.Ticker != primaryTicker
            || existingStock.Name != normalizedName
            || !(existingStock.SecondaryTickers ?? []).SequenceEqual(secondaryTickers);

        if (!needsUpdate)
            return;

        // Pre-check: only a collision against another company's primary ticker blocks us.
        // Secondary-ticker overlap is allowed by the domain.
        if (
            existingStock.Ticker != primaryTicker
            && state.ExistingPrimaryTickers.Contains(primaryTicker)
        )
        {
            // Resolve the holder over every row, not just SEC-feed-scoped
            // ExistingStocks: the holder we need to displace is precisely the
            // one whose own CIK dropped out of the feed, so a feed-scoped lookup
            // would never find it and the obsolete-removal arm below would be
            // unreachable. PrimaryTickerToStock exists for exactly this (see its
            // construction comment) and is what ReplaceObsoleteStock uses.
            state.PrimaryTickerToStock.TryGetValue(primaryTicker, out var tickerHolder);
            if (tickerHolder != null && !state.SecCiks.Contains(tickerHolder.Cik))
            {
                try
                {
                    await DeleteAndUntrack(tickerHolder, state);

                    _logger.LogInformation(
                        "Removed obsolete company {Name} (CIK: {Cik}) holding ticker {Ticker}",
                        tickerHolder.Name,
                        tickerHolder.Cik,
                        primaryTicker
                    );
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Error removing obsolete company for ticker {Ticker}",
                        primaryTicker
                    );
                    await ReportError("CompanySync.RemoveObsolete", ex, $"ticker: {primaryTicker}");
                    return;
                }
            }
            else
            {
                _logger.LogWarning(
                    "Cannot update {OldTicker} to {NewTicker} (CIK: {Cik}) - ticker already in use by active company, skipping",
                    existingStock.Ticker,
                    primaryTicker,
                    secCompany.Cik
                );
                return;
            }
        }

        // Save old values for rollback
        var oldTicker = existingStock.Ticker;
        var oldName = existingStock.Name;
        var oldSecondaryTickers = existingStock.SecondaryTickers.ToList();

        try
        {
            existingStock.Ticker = primaryTicker;
            existingStock.Name = normalizedName;
            existingStock.SecondaryTickers = secondaryTickers;
            await state.CommonStockManager.Update(existingStock);

            if (oldTicker != primaryTicker)
            {
                state.ExistingPrimaryTickers.Remove(oldTicker);
                state.ExistingPrimaryTickers.Add(primaryTicker);
            }
            foreach (var t in oldSecondaryTickers)
                state.ExistingSecondaryTickers.Remove(t);
            foreach (var t in secondaryTickers)
                state.ExistingSecondaryTickers.Add(t);

            _logger.LogDebug(
                "Updated company: {OldTicker} -> {NewTicker}, {OldName} -> {NewName}",
                oldTicker,
                primaryTicker,
                oldName,
                secCompany.Name
            );
        }
        catch (Exception ex)
        {
            // Revert entity to old values and detach changes to prevent dirty state
            existingStock.Ticker = oldTicker;
            existingStock.Name = oldName;
            existingStock.SecondaryTickers = oldSecondaryTickers;
            state.DbContext.Entry(existingStock).State = EntityState.Unchanged;
            _logger.LogError(
                ex,
                "Error updating company {Ticker} - {Name} (CIK: {Cik})",
                primaryTicker,
                secCompany.Name,
                secCompany.Cik
            );
            await ReportError(
                "CompanySync.UpdateStock",
                ex,
                $"ticker: {primaryTicker}, cik: {secCompany.Cik}"
            );
        }
    }

    private async Task ReplaceObsoleteStock(
        CompanyInfo secCompany,
        string primaryTicker,
        List<string> secondaryTickers,
        StockSyncState state
    )
    {
        // The ticker holder may not be in state.ExistingStocks (which is scoped to CIKs in
        // SEC's current feed). Look in the full in-memory map so we also see holders whose
        // own CIK dropped out of the feed.
        state.PrimaryTickerToStock.TryGetValue(primaryTicker, out var obsoleteStock);

        if (obsoleteStock != null && state.SecCiks.Contains(obsoleteStock.Cik))
        {
            // Both CIKs are active in SEC's feed — this is the legitimate parent/subsidiary
            // case (e.g. ATAI Life Sciences + AtaiBeckley sharing ATAI). Resolve which one
            // is the listed parent and attach the loser as a SecondaryCik on the winner
            // so its filings still flow through, without re-warning on future syncs.
            await ResolveTickerCollision(secCompany, obsoleteStock, primaryTicker, state);
            return;
        }

        if (obsoleteStock == null)
        {
            _logger.LogWarning(
                "Company {CompanyName} (CIK: {Cik}) has ticker {Ticker} marked as taken but the holder could not be loaded, skipping",
                secCompany.Name,
                secCompany.Cik,
                primaryTicker
            );
            return;
        }

        try
        {
            await DeleteAndUntrack(obsoleteStock, state);

            var newStock = await state.CommonStockManager.Create(
                new CommonStock
                {
                    Ticker = primaryTicker,
                    Name = NormalizeCompanyName(secCompany.Name),
                    Cik = secCompany.Cik,
                    SecondaryTickers = secondaryTickers,
                    Description = $"Company with tickers: {string.Join(", ", secCompany.Tickers)}",
                    MarketCapitalization = 0,
                    SharesOutStanding = 0,
                }
            );

            state.ExistingCiks.Add(secCompany.Cik);
            state.ExistingPrimaryTickers.Add(primaryTicker);
            foreach (var t in secondaryTickers)
                state.ExistingSecondaryTickers.Add(t);
            state.ExistingStocks.Add(newStock);

            _logger.LogInformation(
                "Replaced obsolete company {OldName} (CIK: {OldCik}) with {NewName} (CIK: {NewCik}) for ticker {Ticker}",
                obsoleteStock.Name,
                obsoleteStock.Cik,
                secCompany.Name,
                secCompany.Cik,
                primaryTicker
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error replacing company for ticker {Ticker}", primaryTicker);
            await ReportError(
                "CompanySync.ReplaceStock",
                ex,
                $"ticker: {primaryTicker}, cik: {secCompany.Cik}"
            );
        }
    }

    private async Task CreateNewStock(
        CompanyInfo secCompany,
        string primaryTicker,
        List<string> secondaryTickers,
        StockSyncState state
    )
    {
        CommonStock newStock = null;
        try
        {
            newStock = await state.CommonStockManager.Create(
                new CommonStock
                {
                    Ticker = primaryTicker,
                    Name = NormalizeCompanyName(secCompany.Name),
                    Cik = secCompany.Cik,
                    SecondaryTickers = secondaryTickers,
                    Description = $"Company with tickers: {string.Join(", ", secCompany.Tickers)}",
                    MarketCapitalization = 0,
                    SharesOutStanding = 0,
                }
            );

            // Add to tracking sets to avoid duplicates in this run
            state.ExistingCiks.Add(secCompany.Cik);
            state.ExistingPrimaryTickers.Add(primaryTicker);
            foreach (var t in secondaryTickers)
                state.ExistingSecondaryTickers.Add(t);
            state.ExistingStocks.Add(newStock);
            _logger.LogDebug(
                "Created new company: {Ticker} - {Name} (CIK: {Cik})",
                primaryTicker,
                secCompany.Name,
                secCompany.Cik
            );
        }
        catch (Exception ex)
        {
            // Detach failed entity to prevent cascading DbContext errors
            if (newStock != null)
            {
                state.DbContext.Entry(newStock).State = EntityState.Detached;
            }
            _logger.LogError(
                ex,
                "Error creating company {Ticker} - {Name} (CIK: {Cik})",
                primaryTicker,
                secCompany.Name,
                secCompany.Cik
            );
            await ReportError(
                "CompanySync.CreateStock",
                ex,
                $"ticker: {primaryTicker}, cik: {secCompany.Cik}"
            );
        }
    }

    private async Task<bool> IsOperatingCompany(CompanyInfo company)
    {
        if (company.EntityType != null)
            return company.IsOperatingCompany;

        var entityType = await _secEdgarClient.GetEntityType(company.Cik);
        company.EntityType = entityType;

        if (!company.IsOperatingCompany)
        {
            _logger.LogDebug(
                "Skipping non-operating entity {Name} (CIK: {Cik}, type: {Type})",
                company.Name,
                company.Cik,
                entityType ?? "unknown"
            );
        }

        return company.IsOperatingCompany;
    }

    /// <summary>
    /// Handles the case where two CIKs in SEC's feed both claim the same primary ticker.
    /// Decides the rightful owner via (listed-on-exchange &gt; operating &gt; older CIK) and
    /// attaches the loser's CIK to the winner's <see cref="CommonStock.SecondaryCiks"/> so
    /// the subsidiary's filings still flow through and we don't re-warn on every sync.
    /// </summary>
    private async Task ResolveTickerCollision(
        CompanyInfo incoming,
        CommonStock incumbent,
        string ticker,
        StockSyncState state
    )
    {
        try
        {
            var incumbentWins = await ShouldIncumbentWin(incoming, incumbent);

            if (incumbentWins)
            {
                if (incumbent.SecondaryCiks.Contains(incoming.Cik))
                    return;

                incumbent.SecondaryCiks = [.. incumbent.SecondaryCiks, incoming.Cik];
                // Save directly via the repository — manager.Update would re-run the full
                // uniqueness validation against the incumbent's own Ticker/CIK, which is
                // an unnecessary round-trip for a SecondaryCiks-only mutation.
                await state.CommonStockRepository.SaveChanges();
                state.SecondaryCikToParent[incoming.Cik] = incumbent;

                _logger.LogInformation(
                    "Attached subsidiary CIK {Cik} ({Name}) to parent {Ticker} (CIK: {ParentCik})",
                    incoming.Cik,
                    incoming.Name,
                    ticker,
                    incumbent.Cik
                );
            }
            else
            {
                // Authoritative signals say the incoming CIK is the rightful holder. We
                // don't auto-swap (that would delete or rewrite the incumbent's history);
                // surface a warning once and rely on operator intervention.
                _logger.LogWarning(
                    "Ticker {Ticker} appears to belong to incoming CIK {IncomingCik} ({IncomingName}) "
                        + "rather than incumbent CIK {IncumbentCik} ({IncumbentName}). Manual review required.",
                    ticker,
                    incoming.Cik,
                    incoming.Name,
                    incumbent.Cik,
                    incumbent.Name
                );
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error resolving ticker collision for {Ticker} between CIKs {IncomingCik} and {IncumbentCik}",
                ticker,
                incoming.Cik,
                incumbent.Cik
            );
            await ReportError(
                "CompanySync.ResolveTickerCollision",
                ex,
                $"ticker: {ticker}, incoming: {incoming.Cik}, incumbent: {incumbent.Cik}"
            );
        }
    }

    private async Task<bool> ShouldIncumbentWin(CompanyInfo incoming, CommonStock incumbent)
    {
        var incomingMeta = await _secEdgarClient.GetCompanyMetadata(incoming.Cik);
        var incumbentMeta = await _secEdgarClient.GetCompanyMetadata(incumbent.Cik);

        // Without authoritative metadata for either side we have no evidence to override
        // the existing assignment — keep the incumbent. Log so operators can investigate
        // patterns of malformed SEC responses that silently force the fallback path.
        if (incomingMeta == null || incumbentMeta == null)
        {
            _logger.LogWarning(
                "Cannot resolve ticker collision deterministically — metadata missing for {MissingSide}: "
                    + "incoming CIK {IncomingCik}, incumbent CIK {IncumbentCik}. Defaulting to incumbent.",
                incomingMeta == null && incumbentMeta == null ? "both"
                    : incomingMeta == null ? "incoming"
                    : "incumbent",
                incoming.Cik,
                incumbent.Cik
            );
            return true;
        }

        if (incomingMeta.IsListed != incumbentMeta.IsListed)
            return incumbentMeta.IsListed;
        if (incomingMeta.IsOperatingCompany != incumbentMeta.IsOperatingCompany)
            return incumbentMeta.IsOperatingCompany;

        return ParseCik(incumbent.Cik) <= ParseCik(incoming.Cik);
    }

    private static long ParseCik(string cik)
    {
        return long.TryParse(cik, out var n) ? n : long.MaxValue;
    }

    // SEC EDGAR returns some names in ALL CAPS (e.g. "AMAZON COM INC",
    // "MICROSOFT CORP") and others in branded mixed case (e.g. "Apple Inc.",
    // "Meta Platforms, Inc."). When a name is uniformly upper-cased we treat it
    // as legacy formatting and convert it to Title Case; mixed-case names are
    // trusted as-is.
    private static string NormalizeCompanyName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return name;

        if (name.Any(char.IsLower))
            return name;

        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(name.ToLowerInvariant());
    }

    // Subsidiaries we already decided about: each entry maps a subsidiary CIK to
    // its parent stock. Incoming SEC entries whose CIK appears here are silently
    // skipped — without this filter every sync would re-evaluate the collision
    // and re-log the warning. Built defensively to survive a data anomaly
    // (the same subsidiary CIK attached to two parents) rather than throwing.
    private Dictionary<string, CommonStock> BuildSecondaryCikToParent(
        List<CommonStock> allExistingStocks
    )
    {
        var secondaryCikToParent = new Dictionary<string, CommonStock>();
        foreach (var stock in allExistingStocks)
        {
            foreach (var subCik in stock.SecondaryCiks)
            {
                if (!secondaryCikToParent.TryAdd(subCik, stock))
                {
                    _logger.LogWarning(
                        "Subsidiary CIK {Cik} is attached to multiple parents ({ExistingParent} and {DuplicateParent}); "
                            + "keeping {ExistingParent}. Manual cleanup required.",
                        subCik,
                        secondaryCikToParent[subCik].Ticker,
                        stock.Ticker
                    );
                }
            }
        }
        return secondaryCikToParent;
    }

    private static async Task DeleteAndUntrack(CommonStock stock, StockSyncState state)
    {
        state.CommonStockRepository.Delete(stock);
        await state.CommonStockRepository.SaveChanges();

        state.ExistingCiks.Remove(stock.Cik);
        state.ExistingPrimaryTickers.Remove(stock.Ticker);
        foreach (var t in stock.SecondaryTickers)
            state.ExistingSecondaryTickers.Remove(t);
        state.ExistingStocks.Remove(stock);
    }

    private Task ReportError(string operation, Exception ex, string context) =>
        _errorReporter.Report(
            ErrorSource.DocumentScraper,
            operation,
            ex.Message,
            ex.StackTrace,
            context
        );

    private class StockSyncState
    {
        public HashSet<string> SecCiks { get; init; }
        public List<CommonStock> ExistingStocks { get; init; }
        public HashSet<string> ExistingCiks { get; init; }
        public HashSet<string> ExistingPrimaryTickers { get; init; }
        public HashSet<string> ExistingSecondaryTickers { get; init; }
        public Dictionary<string, CommonStock> PrimaryTickerToStock { get; init; }
        public Dictionary<string, CommonStock> SecondaryCikToParent { get; init; }
        public CommonStockRepository CommonStockRepository { get; init; }
        public CommonStockManager CommonStockManager { get; init; }
        public DbContext DbContext { get; init; }
    }
}

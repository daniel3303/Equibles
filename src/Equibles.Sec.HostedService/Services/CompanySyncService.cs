using System.Collections.Concurrent;
using System.Data;
using System.Globalization;
using System.Text.RegularExpressions;
using Equibles.CommonStocks.BusinessLogic;
using Equibles.CommonStocks.Data.Helpers;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Core.Configuration;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Integrations.Sec.Contracts;
using Equibles.Integrations.Sec.Models;
using Equibles.Messaging.Contracts.CommonStocks;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Equibles.Sec.HostedService.Services;

public class CompanySyncService : ICompanySyncService
{
    // CIK → when its website lookup last came back blank. SEC's submissions
    // metadata leaves the website field empty for most companies and the scrape
    // cycle repeats every ~15s, so without this memo every blank-website company
    // costs one full EDGAR metadata request per cycle, forever — thousands of
    // requests through the shared rate-limit budget that all return blank again.
    // Static because the service is resolved per cycle; the recheck interval
    // keeps a company that later publishes a website eligible for a refill.
    private static readonly ConcurrentDictionary<string, DateTime> BlankWebsiteCheckedAt = new();
    private static readonly TimeSpan BlankWebsiteRecheckInterval = TimeSpan.FromDays(7);

    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly ISecEdgarClient _secEdgarClient;
    private readonly WorkerOptions _workerOptions;
    private readonly ILogger<CompanySyncService> _logger;
    private readonly ErrorReporter _errorReporter;
    private readonly IBus _bus;

    public CompanySyncService(
        IServiceScopeFactory serviceScopeFactory,
        ISecEdgarClient secEdgarClient,
        IOptions<WorkerOptions> workerOptions,
        ILogger<CompanySyncService> logger,
        ErrorReporter errorReporter,
        IBus bus
    )
    {
        _serviceScopeFactory = serviceScopeFactory;
        _secEdgarClient = secEdgarClient;
        _workerOptions = workerOptions.Value;
        _logger = logger;
        _errorReporter = errorReporter;
        _bus = bus;
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

            using var listingSync = await CommonStockListingSyncLock.Acquire();
            using var scope = _serviceScopeFactory.CreateScope();
            var state = await BuildSyncState(secCompanies, scope);

            foreach (var secCompany in secCompanies)
            {
                var canonicalCik = CikNormalizer.Canonicalize(secCompany.Cik);
                if (canonicalCik == null)
                {
                    _logger.LogWarning(
                        "Company {CompanyName} has an invalid CIK {Cik}, skipping",
                        secCompany.Name,
                        secCompany.Cik
                    );
                    continue;
                }

                if (state.SecondaryCikToParent.TryGetValue(canonicalCik, out var parent))
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

                var normalizedTickers = secCompany
                    .Tickers.Select(TickerNormalizer.NormalizeListed)
                    .Where(ticker => ticker != null)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                var primaryTicker = normalizedTickers.FirstOrDefault(ticker =>
                    ticker.Length <= TickerNormalizer.MaxPrimaryLength
                );
                if (string.IsNullOrEmpty(primaryTicker))
                {
                    _logger.LogWarning(
                        "Company {CompanyName} (CIK: {Cik}) has no tickers, skipping",
                        secCompany.Name,
                        secCompany.Cik
                    );
                    continue;
                }

                var secondaryTickers = normalizedTickers
                    .Where(ticker =>
                        !string.Equals(ticker, primaryTicker, StringComparison.OrdinalIgnoreCase)
                    )
                    .ToList();

                if (state.ExistingCiks.Contains(canonicalCik))
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

    private async Task<StockSyncState> BuildSyncState(
        List<CompanyInfo> secCompanies,
        IServiceScope scope
    )
    {
        var commonStockRepository =
            scope.ServiceProvider.GetRequiredService<CommonStockRepository>();
        var commonStockManager = scope.ServiceProvider.GetRequiredService<CommonStockManager>();
        var dbContext = scope.ServiceProvider.GetRequiredService<EquiblesFinancialDbContext>();
        var secCiks = secCompanies
            .Select(company => CikNormalizer.Canonicalize(company.Cik))
            .Where(cik => cik != null)
            .ToHashSet(StringComparer.Ordinal);

        // Load every existing stock so we can detect subsidiaries already attached
        // as SecondaryCiks on prior syncs. We can't filter by SEC CIKs alone because
        // the subsidiary's CIK won't match any incoming primary CIK — it lives only
        // inside another stock's SecondaryCiks list.
        var allExistingStocks = await commonStockRepository.GetAllIncludingInactive().ToListAsync();
        var existingStocks = allExistingStocks
            .Where(stock => secCiks.Contains(CikNormalizer.Canonicalize(stock.Cik)))
            .ToList();
        var existingCiks = existingStocks
            .Select(stock => CikNormalizer.Canonicalize(stock.Cik))
            .Where(cik => cik != null)
            .ToHashSet(StringComparer.Ordinal);

        // Build the ticker → stock lookup over every row so ReplaceObsoleteStock can find
        // a ticker holder whose own CIK dropped out of SEC's feed but who still owns the
        // primary ticker our incoming company wants. Case-insensitive to match
        // ExistingPrimaryTickers (a casing mismatch made the holder unfindable, wedging
        // the incoming company forever), and last-wins so a duplicate-ticker data
        // anomaly can't throw and abort every future sync cycle.
        var primaryTickerToStock = new Dictionary<string, CommonStock>(
            StringComparer.OrdinalIgnoreCase
        );
        foreach (var stock in allExistingStocks.Where(stock => stock.Active))
        {
            primaryTickerToStock[stock.Ticker] = stock;
        }

        var secondaryCikToParent = BuildSecondaryCikToParent(
            allExistingStocks.Where(stock => stock.Active).ToList()
        );

        var existingPrimaryTickers = (
            await commonStockRepository.GetAllTickers().ToListAsync()
        ).ToHashSet(StringComparer.OrdinalIgnoreCase);

        return new StockSyncState
        {
            SecCiks = secCiks,
            ExistingStocks = existingStocks,
            ExistingCiks = existingCiks,
            ExistingPrimaryTickers = existingPrimaryTickers,
            PrimaryTickerToStock = primaryTickerToStock,
            SecondaryCikToParent = secondaryCikToParent,
            CommonStockRepository = commonStockRepository,
            CommonStockManager = commonStockManager,
            DbContext = dbContext,
        };
    }

    private async Task UpdateExistingStock(
        CompanyInfo secCompany,
        string primaryTicker,
        List<string> secondaryTickers,
        StockSyncState state
    )
    {
        var canonicalCik = CikNormalizer.Canonicalize(secCompany.Cik);
        var existingStock = state.ExistingStocks.First(stock =>
            CikNormalizer.Canonicalize(stock.Cik) == canonicalCik
        );
        var normalizedName = NormalizeCompanyName(secCompany.Name);
        var combinedSecondaryTickers = MergeSecondaryTickers(
            primaryTicker,
            secondaryTickers,
            existingStock.ReferenceTickers
        );
        // Empty string counts as missing: the SEC metadata website field is blank for most
        // companies, and rows that captured that blank must stay eligible for a refill —
        // but only re-ask EDGAR once per recheck interval, not once per 15s cycle.
        var missingWebsite =
            string.IsNullOrEmpty(existingStock.Website)
            && ShouldAttemptWebsiteFetch(secCompany.Cik);
        var needsUpdate =
            !existingStock.Active
            || existingStock.DelistedOn != null
            || existingStock.Ticker != primaryTicker
            || existingStock.Name != normalizedName
            || !(existingStock.SecondaryTickers ?? []).SequenceEqual(combinedSecondaryTickers)
            || missingWebsite;

        if (!needsUpdate)
            return;

        if (!await TryClearPrimaryTickerCollision(secCompany, existingStock, primaryTicker, state))
            return;

        // Save old values for rollback
        var oldTicker = existingStock.Ticker;
        var oldActive = existingStock.Active;
        var oldDelistedOn = existingStock.DelistedOn;
        var oldName = existingStock.Name;
        var oldSecondaryTickers = existingStock.SecondaryTickers.ToList();
        var oldWebsite = existingStock.Website;

        try
        {
            if (missingWebsite)
                existingStock.Website = await FetchWebsite(secCompany.Cik);

            existingStock.Ticker = primaryTicker;
            existingStock.Active = true;
            existingStock.DelistedOn = null;
            existingStock.Name = normalizedName;
            existingStock.SecondaryTickers = combinedSecondaryTickers;

            if (
                existingStock.Ticker == oldTicker
                && existingStock.Active == oldActive
                && existingStock.DelistedOn == oldDelistedOn
                && existingStock.Name == oldName
                && oldSecondaryTickers.SequenceEqual(existingStock.SecondaryTickers)
                && existingStock.Website == oldWebsite
            )
                return;

            // A ticker change orphans every URL published under the old symbol — record it
            // as a redirect alias BEFORE the manager's save so alias and rename commit in
            // the same unit of work. Staged only; the catch below unwinds it on rollback.
            if (oldTicker != primaryTicker)
                await state.CommonStockManager.RecordTickerAlias(existingStock, oldTicker);

            await state.CommonStockManager.Update(existingStock);

            if (oldTicker != primaryTicker)
            {
                state.ExistingPrimaryTickers.Remove(oldTicker);
                state.ExistingPrimaryTickers.Add(primaryTicker);
                state.PrimaryTickerToStock.Remove(oldTicker);
                state.PrimaryTickerToStock[primaryTicker] = existingStock;
            }

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
            existingStock.Active = oldActive;
            existingStock.DelistedOn = oldDelistedOn;
            existingStock.Name = oldName;
            existingStock.SecondaryTickers = oldSecondaryTickers;
            existingStock.Website = oldWebsite;
            state.DbContext.Entry(existingStock).State = EntityState.Unchanged;
            // Unwind any alias changes RecordTickerAlias staged for this failed rename — a
            // pending insert (the new alias) or delete (the last-writer-wins reclaim of a
            // stale one) would otherwise ride along on the NEXT SaveChanges of this
            // long-lived sync context, recording a redirect for a rename that never landed.
            // Earlier stocks in the batch already committed theirs (Update saves per stock),
            // so only this rename's pending entries can be in these states.
            foreach (
                var aliasEntry in state
                    .DbContext.ChangeTracker.Entries<CommonStockTickerAlias>()
                    .Where(e => e.State is EntityState.Added or EntityState.Deleted)
                    .ToList()
            )
            {
                aliasEntry.State =
                    aliasEntry.State == EntityState.Added
                        ? EntityState.Detached
                        : EntityState.Unchanged;
            }
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

    // Clears the way to assign primaryTicker to existingStock. Returns true when the
    // ticker is free to take (no collision, or the obsolete holder was removed); false
    // when the collision can't be resolved and the caller must skip the update.
    private async Task<bool> TryClearPrimaryTickerCollision(
        CompanyInfo secCompany,
        CommonStock existingStock,
        string primaryTicker,
        StockSyncState state
    )
    {
        // Only a collision against another company's primary ticker blocks us.
        // Secondary-ticker overlap is allowed by the domain.
        if (
            existingStock.Ticker == primaryTicker
            || !state.ExistingPrimaryTickers.Contains(primaryTicker)
        )
            return true;

        // Resolve the holder over every row, not just SEC-feed-scoped
        // ExistingStocks: the holder we need to displace is precisely the
        // one whose own CIK dropped out of the feed, so a feed-scoped lookup
        // would never find it and the obsolete-removal arm below would be
        // unreachable. PrimaryTickerToStock exists for exactly this (see its
        // construction comment) and is what ReplaceObsoleteStock uses.
        state.PrimaryTickerToStock.TryGetValue(primaryTicker, out var tickerHolder);
        if (
            tickerHolder != null
            && !state.SecCiks.Contains(CikNormalizer.Canonicalize(tickerHolder.Cik))
        )
        {
            if (HasReferenceCoverage(tickerHolder))
            {
                _logger.LogWarning(
                    "Cannot assign SEC ticker {Ticker} to CIK {IncomingCik}: its incumbent CIK {IncumbentCik} has active reference-directory coverage; preserving the incumbent and its price history",
                    primaryTicker,
                    secCompany.Cik,
                    tickerHolder.Cik
                );
                return false;
            }

            try
            {
                await RetireAndUntrack(tickerHolder, state);

                _logger.LogInformation(
                    "Retired obsolete company {Name} (CIK: {Cik}) holding ticker {Ticker} without deleting its history",
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
                await ReportError("CompanySync.RetireObsolete", ex, $"ticker: {primaryTicker}");
                return false;
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
            return false;
        }

        return true;
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

        if (
            obsoleteStock != null
            && state.SecCiks.Contains(CikNormalizer.Canonicalize(obsoleteStock.Cik))
        )
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

        if (HasReferenceCoverage(obsoleteStock))
        {
            _logger.LogWarning(
                "Cannot replace ticker {Ticker} with SEC CIK {IncomingCik}: incumbent CIK {IncumbentCik} has active reference-directory coverage; preserving the incumbent and its price history",
                primaryTicker,
                secCompany.Cik,
                obsoleteStock.Cik
            );
            return;
        }

        try
        {
            await RetireAndUntrack(obsoleteStock, state);

            var website = await FetchWebsite(secCompany.Cik);
            var newStock = await CreateCommonStock(
                secCompany,
                primaryTicker,
                secondaryTickers,
                state,
                website
            );

            AddAndTrack(newStock, secCompany.Cik, primaryTicker, state);

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
            var website = await FetchWebsite(secCompany.Cik);
            newStock = await CreateCommonStock(
                secCompany,
                primaryTicker,
                secondaryTickers,
                state,
                website
            );

            AddAndTrack(newStock, secCompany.Cik, primaryTicker, state);
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
                await AttachAsSubsidiary(incumbent, incoming, ticker, state);
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

    private async Task AttachAsSubsidiary(
        CommonStock incumbent,
        CompanyInfo incoming,
        string ticker,
        StockSyncState state
    )
    {
        var canonicalIncomingCik = CikNormalizer.Canonicalize(incoming.Cik);
        if (canonicalIncomingCik == null)
            return;

        if (
            incumbent.SecondaryCiks.Any(cik =>
                CikNormalizer.Canonicalize(cik) == canonicalIncomingCik
            )
        )
            return;

        incumbent.SecondaryCiks = [.. incumbent.SecondaryCiks, incoming.Cik];
        // Save directly via the repository — manager.Update would re-run the full
        // uniqueness validation against the incumbent's own Ticker/CIK, which is
        // an unnecessary round-trip for a SecondaryCiks-only mutation.
        await state.CommonStockRepository.SaveChanges();
        state.SecondaryCikToParent[canonicalIncomingCik] = incumbent;

        // Same signal the operator attach publishes (root bus, after the commit):
        // without it the financial-facts checkpoint is never reset, so the newly
        // attached CIK's older facts are skipped until the primary next files.
        await _bus.Publish(
            new StockSecondaryCikAttached(incumbent.Id, incumbent.Ticker, incoming.Cik)
        );

        _logger.LogInformation(
            "Attached subsidiary CIK {Cik} ({Name}) to parent {Ticker} (CIK: {ParentCik})",
            incoming.Cik,
            incoming.Name,
            ticker,
            incumbent.Cik
        );
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

    private static readonly HashSet<string> UpperCaseAbbreviations = new(
        StringComparer.OrdinalIgnoreCase
    )
    {
        "LLC",
        "LLP",
        "LP",
        "PLC",
        "NV",
        "SA",
        "AG",
        "SE",
        "AB",
        "ASA",
        "ETF",
        "ADR",
        "REIT",
        "USA",
        "US",
        "UK",
    };

    private static readonly Regex RomanNumeralPattern = new(
        @"^M{0,3}(CM|CD|D?C{0,3})(XC|XL|L?X{0,3})(IX|IV|V?I{0,3})$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled
    );

    // Three-letter English words that happen to satisfy the Roman regex
    // (MIX=1009, DIV=504, LIV=54, CIV=104). Listing them explicitly keeps
    // legitimate short numerals like XL (40), XC (90), CD (400), CM (900),
    // and combos (XLI, XLV, MII) working.
    private static readonly HashSet<string> RomanNumeralFalsePositives = new(
        StringComparer.OrdinalIgnoreCase
    )
    {
        "MIX",
        "DIV",
        "LIV",
        "CIV",
    };

    private static bool IsRomanNumeral(string token) =>
        RomanNumeralPattern.IsMatch(token) && !RomanNumeralFalsePositives.Contains(token);

    private static string NormalizeCompanyName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return name;

        if (name.Any(char.IsLower))
            return name;

        var titleCased = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(name.ToLowerInvariant());

        var words = titleCased.Split(' ');
        for (var i = 0; i < words.Length; i++)
        {
            var stripped = words[i].TrimStart('(').TrimEnd('.', ',', ';', ')');
            if (
                stripped.Length > 0
                && (UpperCaseAbbreviations.Contains(stripped) || IsRomanNumeral(stripped))
            )
            {
                words[i] = words[i].ToUpperInvariant();
            }
        }

        return string.Join(' ', words);
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
                var canonicalCik = CikNormalizer.Canonicalize(subCik);
                if (canonicalCik == null)
                    continue;

                if (!secondaryCikToParent.TryAdd(canonicalCik, stock))
                {
                    _logger.LogWarning(
                        "Subsidiary CIK {Cik} is attached to multiple parents ({ExistingParent} and {DuplicateParent}); "
                            + "keeping {ExistingParent}. Manual cleanup required.",
                        subCik,
                        secondaryCikToParent[canonicalCik].Ticker,
                        stock.Ticker,
                        secondaryCikToParent[canonicalCik].Ticker
                    );
                }
            }
        }
        return secondaryCikToParent;
    }

    // Test seam: the memo is static (it must outlive the per-cycle service), so
    // suites that exercise the refill path reset it to stay order-independent.
    internal static void ClearBlankWebsiteMemoForTests() => BlankWebsiteCheckedAt.Clear();

    private static bool ShouldAttemptWebsiteFetch(string cik) =>
        !BlankWebsiteCheckedAt.TryGetValue(cik, out var checkedAt)
        || DateTime.UtcNow - checkedAt >= BlankWebsiteRecheckInterval;

    private async Task<string> FetchWebsite(string cik)
    {
        try
        {
            var metadata = await _secEdgarClient.GetCompanyMetadata(cik);
            var website = metadata?.Website?.Trim();
            // Normalise the SEC's usual blank answer to null so it is stored as "still
            // missing" rather than masquerading as a captured website. Remember blanks
            // so the next cycles skip the request until the recheck interval elapses.
            if (string.IsNullOrEmpty(website))
            {
                BlankWebsiteCheckedAt[cik] = DateTime.UtcNow;
                return null;
            }

            BlankWebsiteCheckedAt.TryRemove(cik, out _);
            return website;
        }
        catch (HttpRequestException ex)
        {
            // Transient: leave the memo untouched so the next cycle retries.
            _logger.LogWarning(ex, "Failed to fetch website for CIK {Cik}, skipping", cik);
            return null;
        }
    }

    private static Task<CommonStock> CreateCommonStock(
        CompanyInfo secCompany,
        string primaryTicker,
        List<string> secondaryTickers,
        StockSyncState state,
        string website = null
    ) =>
        state.CommonStockManager.Create(
            new CommonStock
            {
                Ticker = primaryTicker,
                Name = NormalizeCompanyName(secCompany.Name),
                Cik = secCompany.Cik,
                SecondaryTickers = secondaryTickers,
                Description = $"Company with tickers: {string.Join(", ", secCompany.Tickers)}",
                MarketCapitalization = 0,
                SharesOutStanding = 0,
                Website = website,
            }
        );

    internal static List<string> MergeSecondaryTickers(
        string primaryTicker,
        IEnumerable<string> secTickers,
        IEnumerable<string> referenceTickers
    )
    {
        return (secTickers ?? [])
            .Concat(referenceTickers ?? [])
            .Select(TickerNormalizer.NormalizeListed)
            .Where(ticker => ticker != null)
            .Where(ticker =>
                !string.Equals(ticker, primaryTicker, StringComparison.OrdinalIgnoreCase)
            )
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(ticker => ticker, StringComparer.Ordinal)
            .ToList();
    }

    private static bool HasReferenceCoverage(CommonStock stock) =>
        (stock.ReferenceTickers ?? []).Any(ticker =>
            TickerNormalizer.NormalizeListed(ticker) != null
        );

    private static void AddAndTrack(
        CommonStock newStock,
        string cik,
        string primaryTicker,
        StockSyncState state
    )
    {
        var canonicalCik = CikNormalizer.Canonicalize(cik);
        if (canonicalCik != null)
            state.ExistingCiks.Add(canonicalCik);
        state.ExistingPrimaryTickers.Add(primaryTicker);
        state.ExistingStocks.Add(newStock);
        state.PrimaryTickerToStock[primaryTicker] = newStock;
    }

    private static async Task RetireAndUntrack(CommonStock stock, StockSyncState state)
    {
        if (state.DbContext.Database.IsRelational())
        {
            await using var transaction = await state.CommonStockRepository.CreateTransaction(
                IsolationLevel.ReadCommitted
            );

            // BuildSyncState tracks the pre-lock snapshot. Detach it so the locking query
            // materializes the current database values instead of returning that stale instance
            // through EF identity resolution.
            state.DbContext.Entry(stock).State = EntityState.Detached;
            var lockedStock = await state.CommonStockRepository.GetForUpdate(stock.Id);
            if (lockedStock != null)
            {
                if (
                    !string.Equals(lockedStock.Cik, stock.Cik, StringComparison.Ordinal)
                    || !string.Equals(lockedStock.Ticker, stock.Ticker, StringComparison.Ordinal)
                )
                {
                    throw new DbUpdateConcurrencyException(
                        $"CommonStock {stock.Id} changed before its obsolete-row deletion."
                    );
                }

                // A recycled ticker ends the live designation, not the old issuer's identity.
                // Retain the row and every exact price/holding FK; the authoritative inactive
                // directory fills DelistedOn before any historical backfill is attempted.
                lockedStock.Active = false;
                InvalidateHistoricalPriceCompletion(lockedStock);
                await state.CommonStockRepository.SaveChanges();
            }

            await transaction.CommitAsync();
        }
        else
        {
            stock.Active = false;
            InvalidateHistoricalPriceCompletion(stock);
            await state.CommonStockRepository.SaveChanges();
        }

        var canonicalCik = CikNormalizer.Canonicalize(stock.Cik);
        if (canonicalCik != null)
            state.ExistingCiks.Remove(canonicalCik);
        state.ExistingPrimaryTickers.Remove(stock.Ticker);
        state.ExistingStocks.Remove(stock);
        if (
            state.PrimaryTickerToStock.TryGetValue(stock.Ticker, out var mapped)
            && mapped.Id == stock.Id
        )
            state.PrimaryTickerToStock.Remove(stock.Ticker);
    }

    private static void InvalidateHistoricalPriceCompletion(CommonStock stock)
    {
        stock.PriceHistoryBackfilledTickers = stock
            .PriceHistoryBackfilledTickers.Where(ticker =>
                !string.Equals(ticker, stock.Ticker, StringComparison.OrdinalIgnoreCase)
            )
            .ToList();
        stock.HistoricalPriceBackfillAttemptedAt = null;
    }

    private Task ReportError(string operation, Exception ex, string context) =>
        _errorReporter.Report(ErrorSource.DocumentScraper, operation, ex, context);

    private class StockSyncState
    {
        public HashSet<string> SecCiks { get; init; }
        public List<CommonStock> ExistingStocks { get; init; }
        public HashSet<string> ExistingCiks { get; init; }
        public HashSet<string> ExistingPrimaryTickers { get; init; }
        public Dictionary<string, CommonStock> PrimaryTickerToStock { get; init; }
        public Dictionary<string, CommonStock> SecondaryCikToParent { get; init; }
        public CommonStockRepository CommonStockRepository { get; init; }
        public CommonStockManager CommonStockManager { get; init; }
        public DbContext DbContext { get; init; }
    }
}

using Equibles.CommonStocks.Data.Models;
using Equibles.Core.AutoWiring;
using Equibles.Core.Contracts;
using Equibles.CorporateActions.Data;
using Equibles.CorporateActions.Data.Models;
using Equibles.Data;
using Equibles.Holdings.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Holdings.HostedService.Services;

[Service]
public class HoldingsValueRecalculator
{
    private const int MaxRetries = 3;

    // Backoff schedule: retry 1 → 1 day, retry 2 → 1 week, retry 3 → 1 month
    internal static readonly TimeSpan[] RetryDelays =
    [
        TimeSpan.FromDays(1),
        TimeSpan.FromDays(7),
        TimeSpan.FromDays(30),
    ];

    // A row pending longer than the whole ladder has had every retry its age allows. A re-import or
    // repair that restarted its count must not hold its filed value back for another full ladder:
    // a 13F replay once put 4.3M rows pending since May back on the first rung. The full run is one
    // wait per rung up to the last, then the last rung's own wait (1 + 7 + 30 + 30 days), so an
    // unrestarted row settles on its last rung before the age bound applies.
    internal static readonly TimeSpan LadderSpan = RetryDelays.Aggregate(
        RetryDelays[^1],
        (sum, delay) => sum + delay
    );

    // Settling loads each row with its manager legs and re-sums every touched filing, so a large
    // backlog drains over several passes instead of one that holds the bulk cycle for hours.
    internal const int MaxSettledRowsPerPass = 500_000;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IStockPriceProvider _stockPriceProvider;
    private readonly ILogger<HoldingsValueRecalculator> _logger;

    public HoldingsValueRecalculator(
        IServiceScopeFactory scopeFactory,
        IStockPriceProvider stockPriceProvider,
        ILogger<HoldingsValueRecalculator> logger
    )
    {
        _scopeFactory = scopeFactory;
        _stockPriceProvider = stockPriceProvider;
        _logger = logger;
    }

    /// <summary>
    /// Recalculates Value for all holdings with ValuePending = true
    /// where a Yahoo stock price is now available. Uses exponential backoff
    /// (1 day, 1 week, 1 month) and gives up after 3 failed retries, or once a row has been
    /// pending longer than the whole ladder.
    /// </summary>
    public async Task Recalculate(CancellationToken cancellationToken)
    {
        using var lookupScope = _scopeFactory.CreateScope();
        var lookupContext =
            lookupScope.ServiceProvider.GetRequiredService<EquiblesFinancialDbContext>();
        // The pending scan is served by the partial ValuePending index, but a first pass over a
        // large backlog can still outlive the default timeout — and the worker's catch used to
        // swallow that silently, freezing the whole lane.
        ExtendCommandTimeout(lookupContext);

        var pendingPairs = await lookupContext
            .Set<InstitutionalHolding>()
            .Where(h => h.ValuePending && h.ShareType == ShareType.Shares)
            .Select(h => new
            {
                h.EquityIssuerId,
                h.ListedTicker,
                h.ReportDate,
            })
            .Distinct()
            .ToListAsync(cancellationToken);

        if (pendingPairs.Count == 0)
        {
            _logger.LogDebug("No holdings with pending values");
            return;
        }

        _logger.LogInformation(
            "Found {Count} (stock, listing, date) pairs with pending values",
            pendingPairs.Count
        );

        var requests = pendingPairs
            .Select(p => (p.EquityIssuerId, p.ListedTicker, p.ReportDate))
            .ToList();
        var prices = await _stockPriceProvider.GetClosingPrices(requests, cancellationToken);

        _logger.LogInformation(
            "Found prices for {Count}/{Total} pending pairs",
            prices.Count,
            pendingPairs.Count
        );

        var resolvedPairKeys = prices.Keys.ToHashSet();

        // Prices are stored on today's post-split basis, so a pending row's as-filed share count
        // has to be restated before it is priced — the same rule the import applies, and for the
        // same reason (see HoldingValueBasis).
        var pendingStockIds = pendingPairs.Select(p => p.EquityIssuerId).Distinct().ToList();
        var splitsByStock = (
            await StockSplitQueries
                .EffectiveForIssuers(
                    lookupContext,
                    pendingStockIds,
                    DateOnly.FromDateTime(DateTime.UtcNow)
                )
                .ToListAsync(cancellationToken)
        )
            .GroupBy(s => s.EquityIssuerId)
            .ToDictionary(g => g.Key, g => g.ToList());
        var tickerIdentities = await lookupContext
            .Set<EquityIssuer>()
            .Where(cs => pendingStockIds.Contains(cs.Id))
            .Select(cs => new
            {
                cs.Id,
                Ticker = cs.Presentation.Listing.Ticker,
                SecondaryTickers = cs
                    .Securities.SelectMany(nativeSecurity => nativeSecurity.Listings)
                    .Where(nativeListing =>
                        nativeListing.MarketCountryCode == "US"
                        && (
                            nativeListing.IsDirectoryListed
                            && nativeListing.Id != cs.Presentation.EquityListingId
                        )
                    )
                    .Select(nativeListing => nativeListing.Ticker)
                    .ToList(),
            })
            .ToListAsync(cancellationToken);
        var primaryTickers = tickerIdentities.ToDictionary(cs => cs.Id, cs => cs.Ticker);
        var secondaryTickers = tickerIdentities.ToDictionary(
            cs => cs.Id,
            cs => cs.SecondaryTickers ?? []
        );
        var seriesByIssuer = await RegisteredSeriesTickers.Load(
            lookupContext,
            splitsByStock.Values.SelectMany(splits => splits),
            cancellationToken
        );

        var (totalUpdated, deferredPairKeys) = await ResolveHoldingsWithNewPrices(
            prices,
            splitsByStock,
            primaryTickers,
            secondaryTickers,
            seriesByIssuer,
            cancellationToken
        );

        if (deferredPairKeys.Count > 0)
        {
            _logger.LogInformation(
                "Left {Deferred} (stock, date) pair(s) unvalued this pass: an ambiguous split basis or an implausible close blocked an honest derivation",
                deferredPairKeys.Count
            );
        }

        // A pair with a price that could not be used (basis ambiguous, close implausible) has NOT
        // been resolved — it must advance the retry ladder like a priceless pair, or it sits at its
        // current retry count forever. That escape once froze 828,603 rows at ValueRetryCount = 1.
        var unresolvedPairs = pendingPairs
            .Select(p => (p.EquityIssuerId, p.ListedTicker, p.ReportDate))
            .Where(key => !resolvedPairKeys.Contains(key) || deferredPairKeys.Contains(key))
            .ToList();

        // A pair with no row due at `now` loads nothing in the retry loop, so one scan for the due
        // pairs replaces a per-pair query for every pending pair.
        var now = DateTime.UtcNow;
        var duePairs =
            unresolvedPairs.Count == 0
                ? []
                : await LoadDuePairs(lookupContext, now, cancellationToken);
        var totalGivenUp = await IncrementRetryForUnresolved(
            unresolvedPairs.Where(duePairs.Contains).ToList(),
            now,
            cancellationToken
        );

        _logger.LogInformation(
            "Recalculated values for {Updated} holdings, gave up on {GivenUp}",
            totalUpdated,
            totalGivenUp
        );
    }

    private static async Task<
        HashSet<(Guid CommonStockId, string ListedTicker, DateOnly ReportDate)>
    > LoadDuePairs(
        EquiblesFinancialDbContext dbContext,
        DateTime now,
        CancellationToken cancellationToken
    )
    {
        var pairs = await dbContext
            .Set<InstitutionalHolding>()
            .Where(h => h.ValuePending && h.ShareType == ShareType.Shares)
            .Where(RetryDue(now))
            .Select(h => new
            {
                h.EquityIssuerId,
                h.ListedTicker,
                h.ReportDate,
            })
            .Distinct()
            .ToListAsync(cancellationToken);
        return pairs.Select(p => (p.EquityIssuerId, p.ListedTicker, p.ReportDate)).ToHashSet();
    }

    private async Task<int> IncrementRetryForUnresolved(
        IReadOnlyList<(
            Guid CommonStockId,
            string ListedTicker,
            DateOnly ReportDate
        )> unresolvedPairs,
        DateTime now,
        CancellationToken cancellationToken
    )
    {
        var totalGivenUp = 0;
        var deferredExhaustedPairs = 0;
        var settledAccessions = new HashSet<string>();
        var settledReportDates = new HashSet<DateOnly>();
        var exhaustedPairs =
            unresolvedPairs.Count == 0 ? [] : await LoadExhaustedPairs(now, cancellationToken);
        // Newest quarters first, so a pass that spends its settling budget clears the quarters
        // surfaces read most.
        foreach (var pair in unresolvedPairs.OrderByDescending(pair => pair.ReportDate))
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<EquiblesFinancialDbContext>();
            ExtendCommandTimeout(dbContext);
            // Only rows whose ladder step is due are touched; the rest of the pair, and their
            // manager legs, stay on disk until their day comes.
            var dueRows = dbContext
                .Set<InstitutionalHolding>()
                .Where(h =>
                    h.ValuePending
                    && h.ShareType == ShareType.Shares
                    && h.EquityIssuerId == pair.CommonStockId
                    && h.ListedTicker == pair.ListedTicker
                    && h.ReportDate == pair.ReportDate
                )
                .Where(RetryDue(now));

            // Only an exhausted row changes shape and needs its manager legs loaded. It is settled
            // before the climb below so a row stepped onto the last rung now is not re-read.
            if (exhaustedPairs.Contains(pair) && totalGivenUp >= MaxSettledRowsPerPass)
            {
                deferredExhaustedPairs++;
            }
            else if (exhaustedPairs.Contains(pair))
            {
                var exhausted = await dueRows
                    .Where(LadderExhausted(now))
                    .Include(h => h.ManagerEntries)
                    .ToListAsync(cancellationToken);
                foreach (var holding in exhausted)
                {
                    holding.ValueRetryCount++;
                    holding.ValueLastRetryAt = now;
                    // The ladder is exhausted — derivation is not going to happen. Publish the
                    // filer's own figure rather than a silent zero; only a row the filing itself
                    // left unvalued is honestly unknowable.
                    if (holding.FiledValue is > 0)
                    {
                        ApplyFiledValue(holding);
                        settledAccessions.Add(holding.AccessionNumber);
                        settledReportDates.Add(holding.ReportDate);
                    }
                    else
                    {
                        holding.ValuePending = false;
                        holding.ValueUnavailable = true;
                    }
                    totalGivenUp++;
                }
                if (exhausted.Count > 0)
                {
                    await dbContext.SaveChangesAsync(cancellationToken);
                }
            }

            await ClimbOneRung(dbContext, dueRows, now, cancellationToken);
        }
        if (deferredExhaustedPairs > 0)
        {
            _logger.LogInformation(
                "Settled {Settled} exhausted holding(s), this pass's budget; {Deferred} pair(s) with exhausted rows wait for the next pass",
                totalGivenUp,
                deferredExhaustedPairs
            );
        }
        // One rollup pass for everything that settled this cycle — the per-accession re-sum
        // scans every position of every touched filing, so running it once per pair repeated
        // that scan for every pair in the pass.
        await RefreshRollups(settledAccessions, settledReportDates, cancellationToken);
        return totalGivenUp;
    }

    // Every due row that is not exhausted only gains a step and a stamp: one UPDATE per pair,
    // not a load, a tracked change and a statement per row (millions of rows in a retry wave).
    // An exhausted row left for a later pass by the settling budget is not stamped meanwhile.
    private static async Task ClimbOneRung(
        EquiblesFinancialDbContext dbContext,
        IQueryable<InstitutionalHolding> dueRows,
        DateTime now,
        CancellationToken cancellationToken
    )
    {
        var outlived = now - LadderSpan;
        var climbing = dueRows.Where(h =>
            h.ValueRetryCount < MaxRetries && h.CreationTime > outlived
        );
        if (dbContext.Database.IsRelational())
        {
            await climbing.ExecuteUpdateAsync(
                setters =>
                    setters
                        .SetProperty(h => h.ValueRetryCount, h => h.ValueRetryCount + 1)
                        .SetProperty(h => h.ValueLastRetryAt, now),
                cancellationToken
            );
            return;
        }
        // The in-memory provider has no bulk update.
        foreach (var holding in await climbing.ToListAsync(cancellationToken))
        {
            holding.ValueRetryCount++;
            holding.ValueLastRetryAt = now;
        }
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    // The pairs holding a due exhausted row; every other pair needs no entity load.
    private async Task<
        HashSet<(Guid CommonStockId, string ListedTicker, DateOnly ReportDate)>
    > LoadExhaustedPairs(DateTime now, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<EquiblesFinancialDbContext>();
        ExtendCommandTimeout(dbContext);
        var pairs = await dbContext
            .Set<InstitutionalHolding>()
            .Where(h => h.ValuePending && h.ShareType == ShareType.Shares)
            .Where(LadderExhausted(now))
            .Where(RetryDue(now))
            .Select(h => new
            {
                h.EquityIssuerId,
                h.ListedTicker,
                h.ReportDate,
            })
            .Distinct()
            .ToListAsync(cancellationToken);
        return pairs.Select(p => (p.EquityIssuerId, p.ListedTicker, p.ReportDate)).ToHashSet();
    }

    private async Task<(
        int Updated,
        HashSet<(Guid CommonStockId, string ListedTicker, DateOnly Date)> DeferredPairKeys
    )> ResolveHoldingsWithNewPrices(
        Dictionary<(Guid CommonStockId, string ListedTicker, DateOnly Date), decimal> prices,
        Dictionary<Guid, List<StockSplit>> splitsByStock,
        Dictionary<Guid, string> primaryTickers,
        Dictionary<Guid, List<string>> secondaryTickersByStock,
        Dictionary<Guid, IReadOnlyDictionary<string, string>> seriesByIssuer,
        CancellationToken cancellationToken
    )
    {
        var totalUpdated = 0;
        var deferredPairKeys = new HashSet<(Guid, string, DateOnly)>();
        var staleAccessions = new HashSet<string>();
        var staleReportDates = new HashSet<DateOnly>();
        foreach (var ((stockId, listedTicker, reportDate), closePrice) in prices)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // A corrupt stored close prices every position in the stock at once, so the whole
            // pair is refused rather than published; the pair advances the retry ladder and lands
            // on the filed-value fallback when the ladder exhausts.
            if (HoldingValueSanityGuard.IsImplausibleClose(closePrice))
            {
                deferredPairKeys.Add((stockId, listedTicker, reportDate));
                continue;
            }

            splitsByStock.TryGetValue(stockId, out var splits);
            primaryTickers.TryGetValue(stockId, out var primaryTicker);
            secondaryTickersByStock.TryGetValue(stockId, out var secondaryTickers);
            seriesByIssuer.TryGetValue(stockId, out var seriesByTicker);
            if (
                !HoldingValueBasis.TryResolveShareCountFactor(
                    reportDate,
                    splits,
                    listedTicker,
                    primaryTicker,
                    secondaryTickers,
                    out var shareCountFactor,
                    seriesByTicker
                )
            )
            {
                // The stored series straddles two share bases until the split's price adjustment
                // runs. Leave the rows pending rather than publish a value off by the split ratio;
                // the reconciliation stamps the split and a later cycle prices them honestly.
                deferredPairKeys.Add((stockId, listedTicker, reportDate));
                continue;
            }

            // The published per-share figure is factor × close, so a plausible close behind a
            // large restatement factor can still imply an impossible price. Guarding only the
            // raw close would let the derivation through here while the fallback repair keeps
            // resetting it — the two must test the same number or they chase each other forever.
            if (HoldingValueSanityGuard.IsImplausibleClose(shareCountFactor * closePrice))
            {
                deferredPairKeys.Add((stockId, listedTicker, reportDate));
                continue;
            }

            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<EquiblesFinancialDbContext>();
            ExtendCommandTimeout(dbContext);

            var pendingHoldings = await dbContext
                .Set<InstitutionalHolding>()
                .Include(h => h.ManagerEntries)
                .Where(h =>
                    h.ValuePending
                    && h.ShareType == ShareType.Shares
                    && h.EquityIssuerId == stockId
                    && h.ListedTicker == listedTicker
                    && h.ReportDate == reportDate
                )
                .ToListAsync(cancellationToken);

            foreach (var holding in pendingHoldings)
            {
                var derived = holding.Shares * shareCountFactor * closePrice;

                // A per-row basis error (depositary ratio, split the series never captured) shows
                // as a derivation grossly above the filer's own figure — publish the filed value
                // rather than the wrong derivation. A ~1,000× disagreement is the opposite case
                // (the filer still reports thousands) and keeps the derivation.
                if (
                    HoldingValueSanityGuard.ShouldPublishFiledInsteadOfDerived(
                        derived,
                        holding.Shares,
                        holding.FiledValue
                    )
                )
                {
                    ApplyFiledValue(holding);
                    continue;
                }

                // A product past Int64 has no honest published form and no filed figure to fall
                // back on (a filed figure that large would have routed above) — mark the row
                // unknowable rather than publish the silent zero this lane exists to close.
                if (derived > long.MaxValue)
                {
                    holding.ValuePending = false;
                    holding.ValueUnavailable = true;
                    continue;
                }

                holding.Value = ToBoundedValue(holding.Shares, shareCountFactor, closePrice);
                holding.ValuePending = false;
                holding.ValueSource = ValueSource.Derived;

                foreach (var entry in holding.ManagerEntries)
                {
                    entry.Value = ToBoundedValue(entry.Shares, shareCountFactor, closePrice);
                }
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            totalUpdated += pendingHoldings.Count;

            foreach (var holding in pendingHoldings)
            {
                staleAccessions.Add(holding.AccessionNumber);
            }
            if (pendingHoldings.Count > 0)
            {
                staleReportDates.Add(reportDate);
            }
        }

        // Values moved from zero to real figures, so the per-accession rollups and each touched
        // quarter's AUM snapshot are stale until re-derived. One pass for the whole cycle — the
        // re-sum scans every position of every touched filing, so a per-pair refresh repeated
        // that scan for every priced pair.
        await RefreshRollups(staleAccessions, staleReportDates, cancellationToken);
        return (totalUpdated, deferredPairKeys);
    }

    private async Task RefreshRollups(
        HashSet<string> accessions,
        HashSet<DateOnly> reportDates,
        CancellationToken cancellationToken
    )
    {
        if (accessions.Count == 0)
        {
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<EquiblesFinancialDbContext>();
        ExtendCommandTimeout(dbContext);
        await HoldingsRollupRefresher.Refresh(
            dbContext,
            accessions,
            reportDates,
            cancellationToken
        );
    }

    // Per-pair scopes resolve fresh contexts with the default 30s command timeout; a large pair
    // (every institution holding a mega-cap that quarter) needs the same headroom the lookup
    // context gets, or the first heavy pair aborts the whole pass.
    private static void ExtendCommandTimeout(EquiblesFinancialDbContext dbContext)
    {
        if (dbContext.Database.IsRelational())
        {
            dbContext.Database.SetCommandTimeout(TimeSpan.FromMinutes(10));
        }
    }

    // The ladder as a predicate: a row is due once its anchor (last retry, else creation) plus
    // the delay for its retry count has passed, or once it has outlived the whole ladder. Mirrors
    // the in-memory gate over RetryDelays, one branch per step; a fourth step would need a fourth
    // branch here.
    internal static System.Linq.Expressions.Expression<Func<InstitutionalHolding, bool>> RetryDue(
        DateTime now
    )
    {
        var first = now - RetryDelays[0];
        var second = now - RetryDelays[1];
        var later = now - RetryDelays[MaxRetries - 1];
        var outlived = now - LadderSpan;
        return h =>
            (h.ValueRetryCount == 0 && (h.ValueLastRetryAt ?? h.CreationTime) <= first)
            || (h.ValueRetryCount == 1 && (h.ValueLastRetryAt ?? h.CreationTime) <= second)
            || (h.ValueRetryCount >= 2 && (h.ValueLastRetryAt ?? h.CreationTime) <= later)
            || h.CreationTime <= outlived;
    }

    // A due row the unresolved pass settles instead of climbing: on the last rung, or pending
    // since before the whole ladder could have run. CreationTime survives re-imports, so the age
    // bound holds even when a replay restarted the count.
    internal static System.Linq.Expressions.Expression<
        Func<InstitutionalHolding, bool>
    > LadderExhausted(DateTime now)
    {
        var outlived = now - LadderSpan;
        return h => h.ValueRetryCount >= MaxRetries || h.CreationTime <= outlived;
    }

    /// <summary>
    /// Publishes the filer's own reported value on a row no honest derivation can price, splitting
    /// it across manager legs in proportion to their shares (the only allocation the filing
    /// supports — legs carry counts, not values).
    /// </summary>
    internal static void ApplyFiledValue(InstitutionalHolding holding)
    {
        // A Filed row with no filed figure would be a permanently invisible zero: matched by
        // neither repair phase (phase 1 needs FiledValue > 0, phase 2 excludes Filed). Callers
        // must gate on FiledValue is > 0; fail loudly rather than mint such a row.
        if (holding.FiledValue is not > 0)
        {
            throw new ArgumentException(
                "ApplyFiledValue requires a positive FiledValue",
                nameof(holding)
            );
        }

        holding.Value = holding.FiledValue.Value;
        holding.ValuePending = false;
        holding.ValueSource = ValueSource.Filed;

        foreach (var entry in holding.ManagerEntries)
        {
            entry.Value =
                holding.Shares > 0
                    ? (long)(holding.Value * ((decimal)entry.Shares / holding.Shares))
                    : 0L;
        }
    }

    // shares comes from filer-controlled SSHPRNAMT; an oversized count makes the decimal
    // product exceed Int64, so range-check before the cast (mirrors ParseHoldingRow and
    // Filing13DGXmlParser) instead of throwing OverflowException and aborting the batch.
    // The factor restates the as-filed count onto the price's basis and is folded into the
    // product rather than applied to the count first, so a reverse split does not lose the
    // fractional share that rounding the count would discard.
    private static long ToBoundedValue(long shares, decimal shareCountFactor, decimal closePrice)
    {
        var product = shares * shareCountFactor * closePrice;
        return product >= long.MinValue && product <= long.MaxValue ? (long)product : 0L;
    }
}

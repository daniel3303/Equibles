using Equibles.CommonStocks.Data.Models;
using Equibles.Core.AutoWiring;
using Equibles.Core.Contracts;
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
    private static readonly TimeSpan[] RetryDelays =
    [
        TimeSpan.FromDays(1),
        TimeSpan.FromDays(7),
        TimeSpan.FromDays(30),
    ];

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
    /// (1 day, 1 week, 1 month) and gives up after 3 failed retries.
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
            .Where(h => h.ValuePending)
            .Select(h => new
            {
                h.CommonStockId,
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
            .Select(p => (p.CommonStockId, p.ListedTicker, p.ReportDate))
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
        var pendingStockIds = pendingPairs.Select(p => p.CommonStockId).Distinct().ToList();
        var splitsByStock = (
            await lookupContext
                .Set<StockSplit>()
                .Where(s => pendingStockIds.Contains(s.CommonStockId))
                .ToListAsync(cancellationToken)
        )
            .GroupBy(s => s.CommonStockId)
            .ToDictionary(g => g.Key, g => g.ToList());
        var tickerIdentities = await lookupContext
            .Set<CommonStock>()
            .Where(cs => pendingStockIds.Contains(cs.Id))
            .Select(cs => new
            {
                cs.Id,
                cs.Ticker,
                cs.SecondaryTickers,
            })
            .ToListAsync(cancellationToken);
        var primaryTickers = tickerIdentities.ToDictionary(cs => cs.Id, cs => cs.Ticker);
        var secondaryTickers = tickerIdentities.ToDictionary(
            cs => cs.Id,
            cs => cs.SecondaryTickers ?? []
        );

        var (totalUpdated, deferredPairKeys) = await ResolveHoldingsWithNewPrices(
            prices,
            splitsByStock,
            primaryTickers,
            secondaryTickers,
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
            .Select(p => (p.CommonStockId, p.ListedTicker, p.ReportDate))
            .Where(key => !resolvedPairKeys.Contains(key) || deferredPairKeys.Contains(key))
            .ToList();

        var totalGivenUp = await IncrementRetryForUnresolved(
            unresolvedPairs,
            DateTime.UtcNow,
            cancellationToken
        );

        _logger.LogInformation(
            "Recalculated values for {Updated} holdings, gave up on {GivenUp}",
            totalUpdated,
            totalGivenUp
        );
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
        var settledAccessions = new HashSet<string>();
        var settledReportDates = new HashSet<DateOnly>();
        foreach (var pair in unresolvedPairs)
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<EquiblesFinancialDbContext>();
            ExtendCommandTimeout(dbContext);

            var holdings = await dbContext
                .Set<InstitutionalHolding>()
                .Include(h => h.ManagerEntries)
                .Where(h =>
                    h.ValuePending
                    && h.CommonStockId == pair.CommonStockId
                    && h.ListedTicker == pair.ListedTicker
                    && h.ReportDate == pair.ReportDate
                )
                .ToListAsync(cancellationToken);

            var changed = false;
            var settledRows = new List<InstitutionalHolding>();

            foreach (var holding in holdings)
            {
                var delay = RetryDelays[Math.Min(holding.ValueRetryCount, MaxRetries - 1)];
                var anchor = holding.ValueLastRetryAt ?? holding.CreationTime;

                if (anchor.Add(delay) > now)
                    continue;

                holding.ValueRetryCount++;
                holding.ValueLastRetryAt = now;

                if (holding.ValueRetryCount > MaxRetries)
                {
                    // The ladder is exhausted — derivation is not going to happen. Publish the
                    // filer's own figure rather than a silent zero; only a row the filing itself
                    // left unvalued is honestly unknowable.
                    if (holding.FiledValue is > 0)
                    {
                        ApplyFiledValue(holding);
                        settledRows.Add(holding);
                    }
                    else
                    {
                        holding.ValuePending = false;
                        holding.ValueUnavailable = true;
                    }
                    totalGivenUp++;
                }

                changed = true;
            }

            if (changed)
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            foreach (var settled in settledRows)
            {
                settledAccessions.Add(settled.AccessionNumber);
                settledReportDates.Add(settled.ReportDate);
            }
        }

        // One rollup pass for everything that settled this cycle — the per-accession re-sum
        // scans every position of every touched filing, so running it once per pair repeated
        // that scan for every pair in the pass.
        await RefreshRollups(settledAccessions, settledReportDates, cancellationToken);
        return totalGivenUp;
    }

    private async Task<(
        int Updated,
        HashSet<(Guid CommonStockId, string ListedTicker, DateOnly Date)> DeferredPairKeys
    )> ResolveHoldingsWithNewPrices(
        Dictionary<(Guid CommonStockId, string ListedTicker, DateOnly Date), decimal> prices,
        Dictionary<Guid, List<StockSplit>> splitsByStock,
        Dictionary<Guid, string> primaryTickers,
        Dictionary<Guid, List<string>> secondaryTickersByStock,
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
            if (
                !HoldingValueBasis.TryResolveShareCountFactor(
                    reportDate,
                    splits,
                    listedTicker,
                    primaryTicker,
                    secondaryTickers,
                    out var shareCountFactor
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
                    && h.CommonStockId == stockId
                    && h.ListedTicker == listedTicker
                    && h.ReportDate == reportDate
                )
                .ToListAsync(cancellationToken);

            foreach (var holding in pendingHoldings)
            {
                var derived = holding.Shares * shareCountFactor * closePrice;

                // A per-row basis error (depositary ratio, split the series never captured) shows
                // as a derivation grossly above the filer's own figure — publish the filed value
                // rather than the wrong derivation.
                if (HoldingValueSanityGuard.GrosslyExceedsFiled(derived, holding.FiledValue))
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

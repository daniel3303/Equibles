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
        var primaryTickers = await lookupContext
            .Set<CommonStock>()
            .Where(cs => pendingStockIds.Contains(cs.Id))
            .Select(cs => new { cs.Id, cs.Ticker })
            .ToDictionaryAsync(cs => cs.Id, cs => cs.Ticker, cancellationToken);

        var (totalUpdated, totalDeferred) = await ResolveHoldingsWithNewPrices(
            prices,
            splitsByStock,
            primaryTickers,
            cancellationToken
        );

        if (totalDeferred > 0)
        {
            _logger.LogInformation(
                "Left {Deferred} (stock, date) pair(s) pending: a captured split has not had its price adjustment applied, so the share basis is still ambiguous",
                totalDeferred
            );
        }

        var unresolvedPairs = pendingPairs
            .Where(p => !resolvedPairKeys.Contains((p.CommonStockId, p.ListedTicker, p.ReportDate)))
            .Select(p => (p.CommonStockId, p.ListedTicker, p.ReportDate))
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
        IReadOnlyList<(Guid CommonStockId, string ListedTicker, DateOnly ReportDate)> unresolvedPairs,
        DateTime now,
        CancellationToken cancellationToken
    )
    {
        var totalGivenUp = 0;
        foreach (var pair in unresolvedPairs)
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<EquiblesFinancialDbContext>();

            var holdings = await dbContext
                .Set<InstitutionalHolding>()
                .Where(h =>
                    h.ValuePending
                    && h.CommonStockId == pair.CommonStockId
                    && h.ListedTicker == pair.ListedTicker
                    && h.ReportDate == pair.ReportDate
                )
                .ToListAsync(cancellationToken);

            var changed = false;

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
                    holding.ValuePending = false;
                    totalGivenUp++;
                }

                changed = true;
            }

            if (changed)
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }
        }
        return totalGivenUp;
    }

    private async Task<(int Updated, int Deferred)> ResolveHoldingsWithNewPrices(
        Dictionary<(Guid CommonStockId, string ListedTicker, DateOnly Date), decimal> prices,
        Dictionary<Guid, List<StockSplit>> splitsByStock,
        Dictionary<Guid, string> primaryTickers,
        CancellationToken cancellationToken
    )
    {
        var totalUpdated = 0;
        var totalDeferred = 0;
        foreach (var ((stockId, listedTicker, reportDate), closePrice) in prices)
        {
            cancellationToken.ThrowIfCancellationRequested();

            splitsByStock.TryGetValue(stockId, out var splits);
            primaryTickers.TryGetValue(stockId, out var primaryTicker);
            if (
                !HoldingValueBasis.TryResolveShareCountFactor(
                    reportDate,
                    splits,
                    listedTicker,
                    primaryTicker,
                    out var shareCountFactor
                )
            )
            {
                // The stored series straddles two share bases until the split's price adjustment
                // runs. Leave the rows pending rather than publish a value off by the split ratio;
                // the reconciliation stamps the split and a later cycle prices them honestly.
                totalDeferred++;
                continue;
            }

            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<EquiblesFinancialDbContext>();

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
                holding.Value = ToBoundedValue(holding.Shares, shareCountFactor, closePrice);
                holding.ValuePending = false;

                foreach (var entry in holding.ManagerEntries)
                {
                    entry.Value = ToBoundedValue(entry.Shares, shareCountFactor, closePrice);
                }
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            totalUpdated += pendingHoldings.Count;
        }
        return (totalUpdated, totalDeferred);
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

using System.Linq.Expressions;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Core.AutoWiring;
using Equibles.CorporateActions.Data;
using Equibles.CorporateActions.Repositories;
using Equibles.Holdings.Repositories;
using Equibles.Holdings.Repositories.Models;
using Equibles.Yahoo.Data.Models;
using Equibles.Yahoo.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Holdings.BusinessLogic;

/// <summary>
/// Loads exact-listing price returns. Stored closes have no certified split basis, so a
/// captured split across an actual holding or benchmark comparison makes the result unavailable.
/// </summary>
[Service]
public class BacktestPriceLoader
{
    // Forward-fill needs a few trading days of pre-window history so day-zero resolves to the
    // last close even on a weekend or holiday.
    public const int PriceLookbackDays = 14;

    // Bound both expression depth and SQL size. Large multi-quarter portfolios can contain
    // thousands of exact listing keys; one left-deep OR tree risks translator/plan recursion.
    internal const int ListingQueryBatchSize = 64;

    private readonly EquityDailyStockPriceRepository _priceRepository;
    private readonly CommonStockRepository _stockRepository;
    private readonly StockSplitRepository _splitRepository;

    public BacktestPriceLoader(
        EquityDailyStockPriceRepository priceRepository,
        CommonStockRepository stockRepository,
        StockSplitRepository splitRepository
    )
    {
        _priceRepository = priceRepository;
        _stockRepository = stockRepository;
        _splitRepository = splitRepository;
    }

    /// <summary>
    /// Runs a price-return backtest over raw closes. The result intentionally excludes dividends;
    /// callers must label it price return rather than total return.
    /// </summary>
    public async Task<BacktestResult> RunBacktest(
        IReadOnlyList<BacktestQuarterSnapshot> snapshots,
        CommonStock benchmarkStock,
        string benchmarkListedTicker,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default
    )
    {
        var requested = snapshots
            .SelectMany(snapshot => snapshot.Positions)
            .Where(position => !position.IsOption && position.Value > 0)
            .Select(position => new RequestedListing(position.CommonStockId, position.ListedTicker))
            .Distinct()
            .ToList();

        var stockIds = requested
            .Select(request => request.CommonStockId)
            .Append(benchmarkStock.Id)
            .Distinct()
            .ToArray();
        var primaryTickers = await _stockRepository
            .GetByIdsIncludingInactive(stockIds)
            .Select(stock => new { stock.Id, stock.Ticker })
            .ToDictionaryAsync(stock => stock.Id, stock => stock.Ticker, cancellationToken);

        var listingKeys = requested
            .Where(request => primaryTickers.ContainsKey(request.CommonStockId))
            .Select(request => new ListingKey(
                request.CommonStockId,
                NormalizeTicker(request.ListedTicker ?? primaryTickers[request.CommonStockId])
            ))
            .Distinct()
            .ToList();
        var benchmarkKey = new ListingKey(
            benchmarkStock.Id,
            NormalizeTicker(benchmarkListedTicker)
        );
        if (!listingKeys.Contains(benchmarkKey))
            listingKeys.Add(benchmarkKey);

        var priceWindowFrom =
            from > DateOnly.MinValue.AddDays(PriceLookbackDays)
                ? from.AddDays(-PriceLookbackDays)
                : DateOnly.MinValue;
        var splits = await _splitRepository
            .GetAll()
            .Where(split =>
                stockIds.Contains(split.EquityIssuerId)
                && split.EffectiveDate > priceWindowFrom
                && split.EffectiveDate <= to
            )
            .ToListAsync(cancellationToken);

        var splitDatesByListing = new Dictionary<ListingKey, DateOnly[]>();
        foreach (var key in listingKeys)
        {
            var primaryTicker = primaryTickers.GetValueOrDefault(key.CommonStockId);
            var scoped = PriceSeriesSplitScope.ForListing(
                splits.Where(split => split.EquityIssuerId == key.CommonStockId),
                primaryTicker,
                key.ListedTicker
            );
            splitDatesByListing[key] = scoped
                .Select(split => split.EffectiveDate)
                .Distinct()
                .Order()
                .ToArray();
        }

        var requestedKeys = listingKeys.ToHashSet();
        var mappings = await _priceRepository
            .GetLegacyIdentities(listingKeys.Select(key => key.CommonStockId).Distinct())
            .Select(mapping => new
            {
                mapping.CommonStockId,
                mapping.ListedTicker,
                mapping.EquityListingId,
            })
            .ToListAsync(cancellationToken);
        var listingIds = mappings
            .Where(mapping =>
                requestedKeys.Contains(
                    new ListingKey(mapping.CommonStockId, NormalizeTicker(mapping.ListedTicker))
                )
            )
            .Select(mapping => mapping.EquityListingId)
            .Distinct()
            .ToArray();
        var rows = new List<LoadedPriceRow>();
        foreach (var listingBatch in listingIds.Chunk(ListingQueryBatchSize))
        {
            rows.AddRange(
                await _priceRepository
                    .GetAllSeries()
                    .Where(ListingPredicate(listingBatch))
                    .Where(price =>
                        price.Date >= priceWindowFrom
                        && price.Date <= to
                        && price.Close > 0
                        && price.Volume > 0
                    )
                    .Select(price => new LoadedPriceRow
                    {
                        CommonStockId = price.Listing.Security.EquityIssuerId,
                        ListedTicker = price.SourceTicker,
                        Date = price.Date,
                        Close = price.Close,
                    })
                    .ToListAsync(cancellationToken)
            );
        }

        var pricesByListing = rows.Select(row => new
            {
                Key = new ListingKey(row.CommonStockId, NormalizeTicker(row.ListedTicker)),
                row.Date,
                row.Close,
            })
            .Where(row => requestedKeys.Contains(row.Key))
            .GroupBy(row => row.Key)
            .ToDictionary(
                group => group.Key,
                group =>
                    group
                        .OrderBy(row => row.Date)
                        .Select(row => new PriceRow(row.Key.CommonStockId, row.Date, row.Close))
                        .ToArray()
            );

        if (!pricesByListing.TryGetValue(benchmarkKey, out var benchmarkSeries))
            return null;

        var usableFrom = ResolveUsableStart(
            snapshots,
            from,
            to,
            pricesByListing,
            primaryTickers,
            benchmarkSeries
        );
        if (usableFrom == null)
        {
            return new BacktestResult
            {
                StartDate = from,
                EndDate = to,
                Reason = "no common comparable price date for the active portfolio and benchmark",
            };
        }

        if (
            !EveryRebalanceIsPriceable(
                snapshots,
                usableFrom.Value,
                to,
                pricesByListing,
                primaryTickers
            )
        )
        {
            return new BacktestResult
            {
                StartDate = usableFrom.Value,
                EndDate = to,
                Reason =
                    "an in-window rebalance contains a security without a comparable exact-listing price",
            };
        }

        return HoldingsBacktestCalculator.CalculateByListing(
            snapshots,
            usableFrom.Value,
            to,
            priceOf: (stockId, listedTicker, date) =>
            {
                if (!primaryTickers.TryGetValue(stockId, out var primaryTicker))
                    return null;
                var key = new ListingKey(stockId, NormalizeTicker(listedTicker ?? primaryTicker));
                return pricesByListing.TryGetValue(key, out var series)
                    ? ForwardFill(series, date)
                    : null;
            },
            benchmarkPriceOf: date => ForwardFill(benchmarkSeries, date),
            pricesComparable: (stockId, listedTicker, earlier, later) =>
            {
                if (!primaryTickers.TryGetValue(stockId, out var primaryTicker))
                    return false;
                var key = new ListingKey(stockId, NormalizeTicker(listedTicker ?? primaryTicker));
                return AreComparable(
                    pricesByListing[key],
                    splitDatesByListing[key],
                    earlier,
                    later
                );
            },
            benchmarkPricesComparable: (earlier, later) =>
                AreComparable(benchmarkSeries, splitDatesByListing[benchmarkKey], earlier, later)
        );
    }

    public static decimal? ForwardFill(
        Dictionary<Guid, PriceRow[]> pricesByStock,
        Guid stockId,
        DateOnly date
    ) => pricesByStock.TryGetValue(stockId, out var series) ? ForwardFill(series, date) : null;

    // Largest close on or before `date` via binary search; null when the series starts later.
    public static decimal? ForwardFill(PriceRow[] series, DateOnly date) =>
        ForwardFillRow(series, date)?.Price;

    private static bool AreComparable(
        PriceRow[] series,
        DateOnly[] splits,
        DateOnly earlier,
        DateOnly later
    )
    {
        var first = ForwardFillRow(series, earlier);
        return first is { } row && !splits.Any(split => split > row.Date && split <= later);
    }

    private static PriceRow? ForwardFillRow(PriceRow[] series, DateOnly date)
    {
        if (series.Length == 0)
            return null;
        var lo = 0;
        var hi = series.Length - 1;
        var matchIdx = -1;
        while (lo <= hi)
        {
            var mid = (lo + hi) >>> 1;
            if (series[mid].Date <= date)
            {
                matchIdx = mid;
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }
        return matchIdx < 0 ? null : series[matchIdx];
    }

    private static string NormalizeTicker(string ticker) => ticker?.Trim().ToUpperInvariant();

    internal static Expression<Func<EquityDailyStockPrice, bool>> ListingPredicate(
        IReadOnlyCollection<Guid> listingIds
    ) => price => listingIds.Contains(price.EquityListingId);

    // A listing's series can start late, and a first usable close can land after a weekend or
    // holiday. Advance until the benchmark and every security in the then-active snapshot can
    // be priced; repeat when that advance crosses a later rebalance.
    private static DateOnly? ResolveUsableStart(
        IReadOnlyList<BacktestQuarterSnapshot> snapshots,
        DateOnly requestedFrom,
        DateOnly to,
        IReadOnlyDictionary<ListingKey, PriceRow[]> pricesByListing,
        IReadOnlyDictionary<Guid, string> primaryTickers,
        PriceRow[] benchmarkSeries
    )
    {
        if (snapshots.Count == 0)
            return requestedFrom;

        var ordered = snapshots
            .Select(snapshot =>
                (
                    Snapshot: snapshot,
                    RebalanceDate: HoldingsBacktestCalculator.RebalanceDateOf(snapshot.ReportDate)
                )
            )
            .OrderBy(entry => entry.RebalanceDate)
            .ToList();
        var candidate = requestedFrom;

        // Each pass either stabilizes or advances across at least one first-price/rebalance date.
        // The extra two passes cover the initial benchmark and requested-start adjustments.
        var maxPasses = ordered.Count + pricesByListing.Count + 2;
        for (var pass = 0; pass < maxPasses; pass++)
        {
            var priorIndex = ordered.FindLastIndex(entry => entry.RebalanceDate <= candidate);
            var snapshotIndex = priorIndex < 0 ? 0 : priorIndex;
            var active = ordered[snapshotIndex];
            var start = active.RebalanceDate > candidate ? active.RebalanceDate : candidate;
            if (start > to)
                return candidate;

            var next = FirstUsableDate(benchmarkSeries, start);
            if (next == null)
                return null;

            foreach (
                var position in active.Snapshot.Positions.Where(position =>
                    !position.IsOption && position.Value > 0
                )
            )
            {
                if (!primaryTickers.TryGetValue(position.CommonStockId, out var primaryTicker))
                    return null;

                var key = new ListingKey(
                    position.CommonStockId,
                    NormalizeTicker(position.ListedTicker ?? primaryTicker)
                );
                if (
                    !pricesByListing.TryGetValue(key, out var series)
                    || FirstUsableDate(series, start) is not { } firstPriceDate
                )
                {
                    return null;
                }

                if (firstPriceDate > next.Value)
                    next = firstPriceDate;
            }

            if (next.Value == start)
                return start;
            if (next.Value > to)
                return null;

            candidate = next.Value;
        }

        return null;
    }

    private static DateOnly? FirstUsableDate(PriceRow[] series, DateOnly date)
    {
        if (ForwardFill(series, date) is > 0)
            return date;

        foreach (var row in series)
        {
            if (row.Date > date && row.Price > 0)
                return row.Date;
        }
        return null;
    }

    // A later filing can introduce a listing that was not in the initial portfolio. Rebalance uses
    // reported value as its denominator, so silently skipping an unpriced position destroys that
    // weight and publishes a false loss. Fail the result before simulation instead.
    private static bool EveryRebalanceIsPriceable(
        IReadOnlyList<BacktestQuarterSnapshot> snapshots,
        DateOnly from,
        DateOnly to,
        IReadOnlyDictionary<ListingKey, PriceRow[]> pricesByListing,
        IReadOnlyDictionary<Guid, string> primaryTickers
    )
    {
        foreach (var snapshot in snapshots)
        {
            var rebalanceDate = HoldingsBacktestCalculator.RebalanceDateOf(snapshot.ReportDate);
            if (rebalanceDate < from || rebalanceDate > to)
                continue;

            foreach (
                var position in snapshot.Positions.Where(position =>
                    !position.IsOption && position.Value > 0
                )
            )
            {
                if (!primaryTickers.TryGetValue(position.CommonStockId, out var primaryTicker))
                    return false;

                var key = new ListingKey(
                    position.CommonStockId,
                    NormalizeTicker(position.ListedTicker ?? primaryTicker)
                );
                if (
                    !pricesByListing.TryGetValue(key, out var series)
                    || ForwardFill(series, rebalanceDate) is not > 0
                )
                {
                    return false;
                }
            }
        }

        return true;
    }

    private readonly record struct RequestedListing(Guid CommonStockId, string ListedTicker);

    internal readonly record struct ListingKey(Guid CommonStockId, string ListedTicker);

    private sealed class LoadedPriceRow
    {
        public Guid CommonStockId { get; init; }

        public string ListedTicker { get; init; }

        public DateOnly Date { get; init; }

        public decimal Close { get; init; }
    }

    public readonly record struct PriceRow(Guid StockId, DateOnly Date, decimal Price);
}

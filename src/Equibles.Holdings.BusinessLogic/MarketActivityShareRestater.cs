using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Core.AutoWiring;
using Equibles.CorporateActions.Data;
using Equibles.CorporateActions.Data.Models;
using Equibles.CorporateActions.Repositories;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Repositories.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Holdings.BusinessLogic;

// Restates market activity one exact listed price series at a time. Stock-level activity
// deliberately combines sibling share classes for ranking, but a split attributed to one class
// must never multiply the others. The persisted listing breakdown keeps this read path bounded.
[Service]
public class MarketActivityShareRestater
{
    private readonly CommonStockRepository _commonStockRepository;
    private readonly StockSplitRepository _stockSplitRepository;

    public MarketActivityShareRestater(
        CommonStockRepository commonStockRepository,
        StockSplitRepository stockSplitRepository
    )
    {
        _commonStockRepository = commonStockRepository;
        _stockSplitRepository = stockSplitRepository;
    }

    public async Task RestateMarketActivity(
        IReadOnlyList<MarketWideStockActivity> activity,
        DateOnly currentReportDate,
        DateOnly previousReportDate,
        CancellationToken cancellationToken = default
    )
    {
        var stockIds = activity.Select(row => row.CommonStockId).Distinct().ToArray();
        if (stockIds.Length == 0)
            return;

        var primaryTickers = await _commonStockRepository
            .GetByIds(stockIds)
            .AsNoTracking()
            .Select(stock => new { stock.Id, stock.Ticker })
            .ToDictionaryAsync(stock => stock.Id, stock => stock.Ticker, cancellationToken);
        var splitsByStock = (
            await _stockSplitRepository
                .GetAll()
                .AsNoTracking()
                .Where(split => stockIds.Contains(split.CommonStockId))
                .ToListAsync(cancellationToken)
        )
            .GroupBy(split => split.CommonStockId)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<StockSplit>)group.ToList());

        foreach (var row in activity)
        {
            if (!splitsByStock.TryGetValue(row.CommonStockId, out var splits) || splits.Count == 0)
                continue;
            if (!primaryTickers.TryGetValue(row.CommonStockId, out var primaryTicker))
                throw new InvalidOperationException(
                    $"Cannot restate holdings activity for missing stock {row.CommonStockId}."
                );
            if (row.ListingShares.Count == 0)
                throw new InvalidOperationException(
                    $"Cannot restate holdings activity for {primaryTicker} without listing-share snapshots."
                );

            row.CurrentShares = RestateListingTotal(
                row.ListingShares,
                listing => listing.CurrentShares,
                currentReportDate,
                primaryTicker,
                splits
            );
            row.PreviousShares = RestateListingTotal(
                row.ListingShares,
                listing => listing.PreviousShares,
                previousReportDate,
                primaryTicker,
                splits
            );
        }
    }

    public async Task RestateStockActivity(
        CommonStock stock,
        IReadOnlyList<StockQuarterlyActivity> activity,
        CancellationToken cancellationToken = default
    )
    {
        if (activity.Count == 0)
            return;

        var splits = await _stockSplitRepository
            .GetByStock(stock.Id)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        if (splits.Count == 0)
            return;

        foreach (var row in activity)
        {
            if (row.ListingShares.Count == 0)
                throw new InvalidOperationException(
                    $"Cannot restate holdings activity for {stock.Ticker} without listing-share snapshots."
                );

            row.CurrentShares = RestateListingTotal(
                row.ListingShares,
                listing => listing.CurrentShares,
                row.ReportDate,
                stock.Ticker,
                splits
            );
            row.PreviousShares = row.PreviousReportDate is { } previousReportDate
                ? RestateListingTotal(
                    row.ListingShares,
                    listing => listing.PreviousShares,
                    previousReportDate,
                    stock.Ticker,
                    splits
                )
                : 0;
        }
    }

    internal static long RestateListingTotal(
        IReadOnlyList<StockQuarterlyListingActivity> listingShares,
        Func<StockQuarterlyListingActivity, long> shares,
        DateOnly reportDate,
        string primaryTicker,
        IReadOnlyList<StockSplit> splits
    ) =>
        listingShares.Sum(listing =>
            SplitAdjustment.AdjustShareCount(
                shares(listing),
                reportDate,
                PriceSeriesSplitScope.ForListing(splits, primaryTicker, listing.PriceSeriesTicker)
            )
        );
}

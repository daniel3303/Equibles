using Equibles.CommonStocks.Repositories;
using Equibles.Core.AutoWiring;
using Equibles.Holdings.BusinessLogic;
using Equibles.Holdings.Repositories;
using Equibles.Holdings.Repositories.Extensions;
using Equibles.Holdings.Repositories.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Web.Services;

// The quarter-over-quarter read behind the market-wide 13F boards and their CSV. Share counts are
// restated one listed series at a time, so a split between the two quarters is not a trade.
[Service]
public class MarketActivityLoader
{
    private readonly InstitutionalHoldingRepository _holdingRepository;
    private readonly EquityIssuerRepository _commonStockRepository;
    private readonly MarketActivityShareRestater _shareRestater;

    public MarketActivityLoader(
        InstitutionalHoldingRepository holdingRepository,
        EquityIssuerRepository commonStockRepository,
        MarketActivityShareRestater shareRestater
    )
    {
        _holdingRepository = holdingRepository;
        _commonStockRepository = commonStockRepository;
        _shareRestater = shareRestater;
    }

    public async Task<(
        List<MarketWideStockActivity> Activity,
        List<MarketWideStockChurn> Churn
    )> Load(DateOnly selectedDate, DateOnly previousDate, bool combined)
    {
        var activity = await _holdingRepository.GetMarketActivitySnapshotBacked(
            selectedDate,
            previousDate,
            combined
        );

        // The restater reads each stock's primary series from the current directory; a stock
        // outside it keeps its as-filed counts.
        var stockIds = activity.Select(row => row.CommonStockId).ToArray();
        var listed = (
            await _commonStockRepository
                .GetCurrentUsDirectoryByIds(stockIds)
                .Select(stock => stock.Id)
                .ToListAsync()
        ).ToHashSet();
        await _shareRestater.RestateMarketActivity(
            activity.Where(row => listed.Contains(row.CommonStockId)).ToList(),
            selectedDate,
            previousDate
        );

        return (activity, await LoadChurn(selectedDate, previousDate, combined));
    }

    // Snapshot first (the combined lane while the filing window is open), live aggregation only
    // when the matching snapshot is empty.
    private async Task<List<MarketWideStockChurn>> LoadChurn(
        DateOnly selectedDate,
        DateOnly previousDate,
        bool combined
    )
    {
        var snapshot = combined
            ? (
                await _holdingRepository
                    .GetStockActivitySnapshotsCombined(selectedDate)
                    .AsNoTracking()
                    .ToListAsync()
            )
                .Select(row => row.ToChurn())
                .ToList()
            : (
                await _holdingRepository
                    .GetStockActivitySnapshots(selectedDate)
                    .AsNoTracking()
                    .ToListAsync()
            )
                .Select(row => row.ToChurn())
                .ToList();
        if (snapshot.Count > 0)
            return snapshot;

        return await _holdingRepository
            .GetQuarterlyNewSoldOutPositions(selectedDate, previousDate, combined)
            .ToListAsync();
    }
}

using Equibles.CommonStocks.Repositories;
using Equibles.Core.AutoWiring;
using Equibles.CorporateActions.Data;
using Equibles.CorporateActions.Data.Models;
using Equibles.CorporateActions.Repositories;
using Equibles.Holdings.Repositories;
using Equibles.Holdings.Repositories.Models;
using Equibles.Web.Extensions;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Web.Services;

// One page of the double-down board. A split between the two quarters moves every as-filed count
// of that issuer: a forward split reads as growth nobody bought and a reverse split hides real
// growth, so those issuers are judged on restated counts and merged into the ranked page.
[Service]
public class DoubleDownBoardLoader
{
    private readonly InstitutionalHoldingRepository _holdingRepository;
    private readonly EquityIssuerRepository _commonStockRepository;
    private readonly StockSplitRepository _stockSplitRepository;

    public DoubleDownBoardLoader(
        InstitutionalHoldingRepository holdingRepository,
        EquityIssuerRepository commonStockRepository,
        StockSplitRepository stockSplitRepository
    )
    {
        _holdingRepository = holdingRepository;
        _commonStockRepository = commonStockRepository;
        _stockSplitRepository = stockSplitRepository;
    }

    public async Task<(List<DoubleDownPosition> Positions, int TotalCount)> LoadPage(
        DateOnly selectedDate,
        DateOnly previousDate,
        double minPctIncrease,
        bool combined,
        int page,
        int pageSize
    )
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var splitIssuerIds = await _stockSplitRepository
            .GetEffective(today)
            .Where(split => split.EffectiveDate > previousDate && split.EffectiveDate <= selectedDate)
            .Select(split => split.EquityIssuerId)
            .Distinct()
            .ToListAsync();

        var unsplit = _holdingRepository
            .GetDoubleDownPositions(selectedDate, previousDate, minPctIncrease, combined)
            .Where(position => !splitIssuerIds.Contains(position.CommonStockId))
            .OrderByDescending(position =>
                (double)(position.CurrentShares - position.PreviousShares) / position.PreviousShares
            );
        var unsplitCount = await unsplit.CountAsync();
        if (splitIssuerIds.Count == 0)
            return (await unsplit.Page(page, pageSize).ToListAsync(), unsplitCount);

        var restated = await LoadRestated(
            selectedDate,
            previousDate,
            minPctIncrease,
            splitIssuerIds,
            today
        );

        var total = unsplitCount + restated.Count;
        var skip = (long)(page - 1) * pageSize;
        if (skip >= total)
            return ([], total);

        // The page's rows sit within the first page * pageSize rows of either ranked list.
        var positions = (await unsplit.Take((int)skip + pageSize).ToListAsync())
            .Concat(restated)
            .OrderByDescending(position => position.PctChange)
            .Skip((int)skip)
            .Take(pageSize)
            .ToList();
        return (positions, total);
    }

    private async Task<List<DoubleDownPosition>> LoadRestated(
        DateOnly selectedDate,
        DateOnly previousDate,
        double minPctIncrease,
        List<Guid> splitIssuerIds,
        DateOnly today
    )
    {
        var candidates = await _holdingRepository
            .GetDoubleDownCandidates(selectedDate, previousDate, splitIssuerIds)
            .ToListAsync();
        var primaryTickers = await _commonStockRepository
            .GetCurrentUsDirectoryByIds(splitIssuerIds)
            .AsNoTracking()
            .Select(stock => new { stock.Id, Ticker = stock.Presentation.Listing.Ticker })
            .ToDictionaryAsync(stock => stock.Id, stock => stock.Ticker);
        var splitsByIssuer = (
            await _stockSplitRepository
                .GetEffective(today)
                .AsNoTracking()
                .Where(split => splitIssuerIds.Contains(split.EquityIssuerId))
                .ToListAsync()
        )
            .GroupBy(split => split.EquityIssuerId)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<StockSplit>)group.ToList());

        var restated = new List<DoubleDownPosition>();
        foreach (var position in candidates)
        {
            // Without a primary series the splits cannot be assigned to this row's series.
            if (!primaryTickers.TryGetValue(position.CommonStockId, out var primaryTicker))
                continue;
            var splits = ShareCountSplitScope.ForListedSeries(
                splitsByIssuer[position.CommonStockId],
                primaryTicker,
                position.ListedTicker ?? primaryTicker
            );
            position.CurrentShares = SplitAdjustment.AdjustShareCount(
                position.CurrentShares,
                selectedDate,
                splits
            );
            position.PreviousShares = SplitAdjustment.AdjustShareCount(
                position.PreviousShares,
                previousDate,
                splits
            );
            if (
                position.PreviousShares > 0
                && position.CurrentShares > position.PreviousShares
                && position.PctChange >= minPctIncrease
            )
                restated.Add(position);
        }
        return restated;
    }
}

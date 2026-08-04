using System.Data;
using Equibles.CommonStocks.Repositories;
using Equibles.Core.AutoWiring;
using Equibles.CorporateActions.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Equibles.CorporateActions.BusinessLogic;

// Split-side coordinator for the price back-adjustment pass. Owns the two data
// operations the worker drives around its Yahoo fetch: selecting which stocks
// still have unreconciled splits (capped per cycle) and stamping a stock's
// pending splits once its prices have been re-synced. Kept off the worker so it
// is unit-testable without a live scraper, and off the repository so the repo
// stays pure data-access.
[Service]
public class SplitPriceReconciliationManager
{
    private readonly StockSplitRepository _splitRepository;
    private readonly CommonStockRepository _stockRepository;

    public SplitPriceReconciliationManager(
        StockSplitRepository splitRepository,
        CommonStockRepository stockRepository
    )
    {
        _splitRepository = splitRepository;
        _stockRepository = stockRepository;
    }

    /// <summary>
    /// Returns the distinct exact listed series with at least one unreconciled split, capped at
    /// <paramref name="maxPerCycle"/> so the universe backfill throttles against Yahoo's shared
    /// limiter. The remainder is reported (<see cref="PendingSplitSelection.Skipped"/>) rather than
    /// silently dropped — it is picked up on a later cycle. A non-positive cap selects all.
    /// </summary>
    public async Task<PendingSplitSelection> SelectPendingSeries(int maxPerCycle)
    {
        var pendingRows = await _splitRepository
            .GetPendingPriceAdjustment()
            .Where(split => split.PriceSeriesTicker != null)
            .Select(split => new
            {
                split.Id,
                split.CommonStockId,
                split.PriceSeriesTicker,
                split.EffectiveDate,
                split.Numerator,
                split.Denominator,
                split.Source,
            })
            .OrderBy(split => split.CommonStockId)
            .ThenBy(split => split.PriceSeriesTicker)
            .ThenBy(split => split.EffectiveDate)
            .ThenBy(split => split.Id)
            .ToListAsync();
        var pendingSeries = pendingRows
            .GroupBy(row => new { row.CommonStockId, row.PriceSeriesTicker })
            .Select(group => new PendingSplitSeries(
                group.Key.CommonStockId,
                group.Key.PriceSeriesTicker,
                group
                    .Select(row => new PendingSplitSnapshot(
                        row.Id,
                        row.EffectiveDate,
                        row.Numerator,
                        row.Denominator,
                        row.Source
                    ))
                    .ToList()
            ))
            .ToList();

        var selected = maxPerCycle > 0 ? pendingSeries.Take(maxPerCycle).ToList() : pendingSeries;

        return new PendingSplitSelection(
            selected,
            pendingSeries.Count,
            pendingSeries.Count - selected.Count
        );
    }

    /// <summary>
    /// Stamps only the selected splits whose complete source state is still unchanged after the
    /// Yahoo fetch. The parent stock lock serializes current capture, and each selected split row
    /// is locked against a retiring worker before comparison. A new or revised split therefore
    /// stays pending for another full-history fetch.
    /// </summary>
    public async Task<int> StampApplied(
        PendingSplitSeries selectedSeries,
        DateTime appliedTime,
        CancellationToken cancellationToken = default
    )
    {
        await using var transaction = await _splitRepository.CreateTransaction(
            IsolationLevel.ReadCommitted,
            cancellationToken
        );
        var stock = await _stockRepository.GetForUpdate(
            selectedSeries.CommonStockId,
            cancellationToken
        );
        if (stock == null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return 0;
        }

        var selectedById = selectedSeries.Splits.ToDictionary(split => split.Id);
        var locked = await _splitRepository.GetForUpdate(selectedById.Keys, cancellationToken);
        var pending = locked
            .Where(s =>
                s.CommonStockId == selectedSeries.CommonStockId
                && s.PriceSeriesTicker == selectedSeries.ListedTicker
                && s.PriceAdjustmentAppliedTime == null
            )
            .ToList();

        var unchanged = pending
            .Where(split =>
            {
                var selected = selectedById[split.Id];
                return split.EffectiveDate == selected.EffectiveDate
                    && split.Numerator == selected.Numerator
                    && split.Denominator == selected.Denominator
                    && split.Source == selected.Source;
            })
            .ToList();

        foreach (var split in unchanged)
            split.PriceAdjustmentAppliedTime = appliedTime;

        if (unchanged.Count > 0)
            await _splitRepository.SaveChanges();

        await transaction.CommitAsync(cancellationToken);
        return unchanged.Count;
    }
}

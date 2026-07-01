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

    public SplitPriceReconciliationManager(StockSplitRepository splitRepository)
    {
        _splitRepository = splitRepository;
    }

    /// <summary>
    /// Returns the distinct stocks with at least one unreconciled split, capped at
    /// <paramref name="maxPerCycle"/> so the universe backfill throttles against Yahoo's shared
    /// limiter. The remainder is reported (<see cref="PendingSplitSelection.Skipped"/>) rather than
    /// silently dropped — it is picked up on a later cycle. A non-positive cap selects all.
    /// </summary>
    public async Task<PendingSplitSelection> SelectPendingStocks(int maxPerCycle)
    {
        var pendingStockIds = await _splitRepository
            .GetPendingPriceAdjustment()
            .Select(s => s.CommonStockId)
            .Distinct()
            .OrderBy(id => id)
            .ToListAsync();

        var selected =
            maxPerCycle > 0 ? pendingStockIds.Take(maxPerCycle).ToList() : pendingStockIds;

        return new PendingSplitSelection(
            selected,
            pendingStockIds.Count,
            pendingStockIds.Count - selected.Count
        );
    }

    /// <summary>
    /// Stamps every currently-unreconciled split for the stock as applied at
    /// <paramref name="appliedTime"/>. Idempotent: already-stamped splits are untouched, so a
    /// second pass over the same stock stamps nothing. Returns how many splits were stamped.
    /// </summary>
    public async Task<int> StampApplied(Guid commonStockId, DateTime appliedTime)
    {
        var pending = await _splitRepository
            .GetByStock(commonStockId)
            .Where(s => s.PriceAdjustmentAppliedTime == null)
            .ToListAsync();

        if (pending.Count == 0)
            return 0;

        foreach (var split in pending)
            split.PriceAdjustmentAppliedTime = appliedTime;

        await _splitRepository.SaveChanges();
        return pending.Count;
    }
}

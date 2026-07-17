using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Repositories.Models;

namespace Equibles.Holdings.Repositories;

public static class IndustryAllocationCalculator
{
    // currentQuarterHoldings MUST be materialized with the CommonStock.Industry navigation
    // populated (Include(h => h.CommonStock.Industry) at the query site) — the calculator
    // only reads the loaded references, never triggers lazy loads.
    public static List<IndustryAllocationSlice> Calculate(
        IReadOnlyList<InstitutionalHolding> currentQuarterHoldings
    ) =>
        Calculate(
            currentQuarterHoldings,
            h => h.CommonStock?.IndustryId,
            h => h.CommonStock?.Industry?.Name
        );

    // Broad-sector rollup of the same allocation: groups by the industry's Sector instead of
    // the fine industry taxonomy, so "is this fund concentrated in tech?" is answered in one
    // ~10-row table instead of summing dozens of industries. Requires the query site to also
    // populate CommonStock.Industry.Sector (ThenInclude at the call site). The slice reuses
    // the IndustryAllocationSlice shape — IndustryId/IndustryName carry the sector id/name.
    public static List<IndustryAllocationSlice> CalculateBySector(
        IReadOnlyList<InstitutionalHolding> currentQuarterHoldings
    ) =>
        Calculate(
            currentQuarterHoldings,
            h => h.CommonStock?.Industry?.SectorId,
            h => h.CommonStock?.Industry?.Sector?.Name
        );

    private static List<IndustryAllocationSlice> Calculate(
        IReadOnlyList<InstitutionalHolding> currentQuarterHoldings,
        Func<InstitutionalHolding, Guid?> groupKey,
        Func<InstitutionalHolding, string> groupName
    )
    {
        if (currentQuarterHoldings.Count == 0)
            return [];

        var totalValue = currentQuarterHoldings.Sum(h => h.Value);

        // Group by taxonomy id (null collapses into one bucket); the per-stock aggregation
        // below ensures a holder reporting the same stock across multiple discretion rows
        // is counted as ONE position in the allocation, not multiple.
        var slices = currentQuarterHoldings
            .GroupBy(groupKey)
            .Select(group =>
            {
                var groupId = group.Key;
                var name =
                    group.Select(groupName).FirstOrDefault(n => n != null)
                    ?? IndustryAllocationSlice.UnclassifiedName;
                var perStock = group
                    .GroupBy(h => h.CommonStockId)
                    .Select(stockGroup => new { Value = stockGroup.Sum(h => h.Value) })
                    .ToList();
                var groupValue = perStock.Sum(p => p.Value);
                return new IndustryAllocationSlice
                {
                    IndustryId = groupId,
                    IndustryName = name,
                    PositionCount = perStock.Count,
                    TotalValue = groupValue,
                    PercentOfPortfolio = Percentage.Of(groupValue, totalValue),
                };
            })
            // Unclassified always last, even if its value would otherwise rank it higher.
            .OrderBy(s => s.IndustryId == null ? 1 : 0)
            .ThenByDescending(s => s.TotalValue)
            .ToList();

        return slices;
    }
}

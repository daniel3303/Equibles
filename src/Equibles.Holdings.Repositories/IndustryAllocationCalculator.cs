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
    )
    {
        if (currentQuarterHoldings.Count == 0)
            return [];

        var totalValue = currentQuarterHoldings.Sum(h => h.Value);

        // Group by industry id (null collapses into one bucket); the per-stock aggregation
        // below ensures a holder reporting the same stock across multiple discretion rows
        // is counted as ONE position in the allocation, not multiple.
        var byIndustry = currentQuarterHoldings
            .GroupBy(h => h.CommonStock?.IndustryId)
            .Select(industryGroup =>
            {
                var industryId = industryGroup.Key;
                var industryName =
                    industryGroup.FirstOrDefault()?.CommonStock?.Industry?.Name
                    ?? IndustryAllocationSlice.UnclassifiedName;
                var perStock = industryGroup
                    .GroupBy(h => h.CommonStockId)
                    .Select(stockGroup => new { Value = stockGroup.Sum(h => h.Value) })
                    .ToList();
                var industryValue = perStock.Sum(p => p.Value);
                return new IndustryAllocationSlice
                {
                    IndustryId = industryId,
                    IndustryName = industryName,
                    PositionCount = perStock.Count,
                    TotalValue = industryValue,
                    PercentOfPortfolio =
                        totalValue > 0 ? (double)industryValue / totalValue * 100.0 : 0,
                };
            })
            // Unclassified always last, even if its value would otherwise rank it higher.
            .OrderBy(s => s.IndustryId == null ? 1 : 0)
            .ThenByDescending(s => s.TotalValue)
            .ToList();

        return byIndustry;
    }
}

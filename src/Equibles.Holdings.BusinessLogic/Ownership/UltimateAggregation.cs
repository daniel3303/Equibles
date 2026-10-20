using Equibles.Holdings.BusinessLogic.Ownership.Models;
using Equibles.Holdings.Data.Models;

namespace Equibles.Holdings.BusinessLogic.Ownership;

/// <summary>
/// Groups the summed rows under each holder's ultimate parent (ULT-AGG): 13F filers, which the 13F
/// rules keep disjoint, plus the insider and 13D/G-only blocks no 13F report of the listing
/// contains. The groups therefore add up to the summary's summed lines.
/// </summary>
public static class UltimateAggregation
{
    public static List<UltimateHolderGroup> Build(
        IReadOnlyList<StockHolderRow> rows,
        IReadOnlyDictionary<string, HolderRollup> rollups,
        Func<string, string> nameOfCik,
        long? sharesOutstanding
    )
    {
        var summed = rows.Where(row => !row.Has(StockHolderRowFlags.NotSummed)).ToList();
        foreach (var row in summed)
            row.RollupCik = UltimateOf(row, rollups);
        return summed
            .GroupBy(row => row.RollupCik ?? row.RowKey)
            .Select(group =>
            {
                var members = StockOwnershipPrecedence.Ordered(group).ToList();
                var shares = members.Sum(row => row.Shares);
                var own = members.FirstOrDefault(row => row.Cik == group.Key);
                return new UltimateHolderGroup
                {
                    UltimateCik = members[0].RollupCik,
                    Name = own?.Name ?? nameOfCik(group.Key) ?? members[0].Name,
                    Shares = shares,
                    Form13FShares = members
                        .Where(row => row.Source == StockHolderSource.Form13F)
                        .Sum(row => row.Shares),
                    PercentOutstanding = sharesOutstanding is > 0
                        ? Math.Round(100m * shares / sharesOutstanding.Value, 4)
                        : null,
                    Members = members,
                };
            })
            .OrderByDescending(group => group.Shares)
            .ThenBy(group => group.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(group => group.UltimateCik, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>The ultimate parent CIK of a row's holder: its stored rollup, else the holder itself.</summary>
    public static string UltimateOf(
        StockHolderRow row,
        IReadOnlyDictionary<string, HolderRollup> rollups
    ) =>
        row.Cik == null ? null
        : rollups.TryGetValue(row.Cik, out var rollup) && rollup.UltimateCik != null
            ? rollup.UltimateCik
        : row.Cik;
}

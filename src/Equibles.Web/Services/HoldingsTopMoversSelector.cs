using Equibles.Web.ViewModels.Stocks;

namespace Equibles.Web.Services;

public static class HoldingsTopMoversSelector
{
    public static (List<HolderPositionChange> Buyers, List<HolderPositionChange> Sellers) Select(
        Dictionary<PositionChangeType, List<HolderPositionChange>> groupedHolders,
        int max
    )
    {
        if (max <= 0)
            return ([], []);

        var buyers = EnumerateOrEmpty(groupedHolders, PositionChangeType.New)
            .Concat(EnumerateOrEmpty(groupedHolders, PositionChangeType.Increased))
            .OrderByDescending(e => e.DeltaShares)
            .Take(max)
            .ToList();

        var sellers = EnumerateOrEmpty(groupedHolders, PositionChangeType.Reduced)
            .Concat(EnumerateOrEmpty(groupedHolders, PositionChangeType.SoldOut))
            .OrderBy(e => e.DeltaShares)
            .Take(max)
            .ToList();

        return (buyers, sellers);
    }

    public static int CountBuyers(
        Dictionary<PositionChangeType, List<HolderPositionChange>> groupedHolders
    ) => CountIn(groupedHolders, PositionChangeType.New, PositionChangeType.Increased);

    public static int CountSellers(
        Dictionary<PositionChangeType, List<HolderPositionChange>> groupedHolders
    ) => CountIn(groupedHolders, PositionChangeType.Reduced, PositionChangeType.SoldOut);

    private static int CountIn(
        Dictionary<PositionChangeType, List<HolderPositionChange>> groupedHolders,
        params PositionChangeType[] types
    )
    {
        var total = 0;
        foreach (var type in types)
        {
            if (groupedHolders.TryGetValue(type, out var list))
                total += list.Count;
        }
        return total;
    }

    private static IEnumerable<HolderPositionChange> EnumerateOrEmpty(
        Dictionary<PositionChangeType, List<HolderPositionChange>> groupedHolders,
        PositionChangeType type
    ) => groupedHolders.TryGetValue(type, out var list) ? list : [];
}

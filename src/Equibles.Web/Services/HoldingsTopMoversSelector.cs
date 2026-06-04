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

        var buyers = TopBy(
            groupedHolders,
            max,
            descending: true,
            PositionChangeType.New,
            PositionChangeType.Increased
        );

        var sellers = TopBy(
            groupedHolders,
            max,
            descending: false,
            PositionChangeType.Reduced,
            PositionChangeType.SoldOut
        );

        return (buyers, sellers);
    }

    // Largest movers across the given change types, by share delta. Buyers rank
    // descending (biggest accumulation first), sellers ascending (biggest reduction first).
    private static List<HolderPositionChange> TopBy(
        Dictionary<PositionChangeType, List<HolderPositionChange>> groupedHolders,
        int max,
        bool descending,
        params PositionChangeType[] types
    )
    {
        var combined = types.SelectMany(type => EnumerateOrEmpty(groupedHolders, type));
        var ordered = descending
            ? combined.OrderByDescending(e => e.DeltaShares)
            : combined.OrderBy(e => e.DeltaShares);
        return ordered.Take(max).ToList();
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
    ) => types.Sum(type => groupedHolders.TryGetValue(type, out var list) ? list.Count : 0);

    private static IEnumerable<HolderPositionChange> EnumerateOrEmpty(
        Dictionary<PositionChangeType, List<HolderPositionChange>> groupedHolders,
        PositionChangeType type
    ) => groupedHolders.TryGetValue(type, out var list) ? list : [];
}

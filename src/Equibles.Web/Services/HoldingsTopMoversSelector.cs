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
        var combined = Movers(groupedHolders, buying: descending, types);
        var ordered = descending
            ? combined.OrderByDescending(e => e.DeltaShares)
            : combined.OrderBy(e => e.DeltaShares);
        return ordered.Take(max).ToList();
    }

    public static int CountBuyers(
        Dictionary<PositionChangeType, List<HolderPositionChange>> groupedHolders
    ) =>
        Movers(groupedHolders, buying: true, PositionChangeType.New, PositionChangeType.Increased)
            .Count();

    public static int CountSellers(
        Dictionary<PositionChangeType, List<HolderPositionChange>> groupedHolders
    ) =>
        Movers(
                groupedHolders,
                buying: false,
                PositionChangeType.Reduced,
                PositionChangeType.SoldOut
            )
            .Count();

    // A mover changed its share count. Shares are common shares only, so a holder whose legs
    // are all options enters or leaves at zero shares: it is in its bucket, and is neither a
    // buyer nor a seller.
    private static IEnumerable<HolderPositionChange> Movers(
        Dictionary<PositionChangeType, List<HolderPositionChange>> groupedHolders,
        bool buying,
        params PositionChangeType[] types
    ) =>
        types
            .SelectMany(type => EnumerateOrEmpty(groupedHolders, type))
            .Where(change => buying ? change.DeltaShares > 0 : change.DeltaShares < 0);

    private static IEnumerable<HolderPositionChange> EnumerateOrEmpty(
        Dictionary<PositionChangeType, List<HolderPositionChange>> groupedHolders,
        PositionChangeType type
    ) => groupedHolders.TryGetValue(type, out var list) ? list : [];
}

namespace Equibles.Holdings.BusinessLogic.Intelligence.Models;

/// <summary>
/// One filer's common-share book for a quarter as the metrics read it, kept only while the stock
/// signals and overlap fold it in: the positions on both sides (the quarter before already on this
/// quarter's basis), the securities whose split basis could not be resolved, each held security's
/// tenure and cost slot, and the priced trades.
/// </summary>
public sealed record HolderQuarterBook
{
    public IReadOnlyList<HolderEquityPosition> Current { get; init; } = [];

    /// <summary>Null when the filer had no comparable 13F the quarter before.</summary>
    public IReadOnlyList<HolderEquityPosition> Previous { get; init; }

    public IReadOnlySet<HolderSecurityKey> UnresolvedBasis { get; init; } =
        new HashSet<HolderSecurityKey>();

    public IReadOnlyDictionary<HolderSecurityKey, HolderTenure> Tenures { get; init; } =
        new Dictionary<HolderSecurityKey, HolderTenure>();

    public IReadOnlyDictionary<HolderSecurityKey, HolderCostSlot> CostSlots { get; init; } =
        new Dictionary<HolderSecurityKey, HolderCostSlot>();

    public IReadOnlyList<HolderSecurityTrade> SecurityTrades { get; init; } = [];
}

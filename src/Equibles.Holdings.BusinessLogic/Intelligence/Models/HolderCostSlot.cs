namespace Equibles.Holdings.BusinessLogic.Intelligence.Models;

/// <summary>
/// One held position's estimated cost per share at a quarter end, on that quarter's share basis;
/// <see cref="CostPerShare"/> is set only when <see cref="Status"/> is Estimated.
/// </summary>
public readonly record struct HolderCostSlot(HolderCostStatus Status, decimal? CostPerShare)
{
    public static HolderCostSlot Estimated(decimal costPerShare) =>
        new(HolderCostStatus.Estimated, costPerShare);

    public static readonly HolderCostSlot UnknownEntry = new(HolderCostStatus.UnknownEntry, null);
    public static readonly HolderCostSlot Unpriced = new(HolderCostStatus.Unpriced, null);
}

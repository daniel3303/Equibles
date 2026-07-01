namespace Equibles.CorporateActions.BusinessLogic;

/// <summary>
/// The set of stocks the split-price back-adjustment pass will re-sync this cycle.
/// <see cref="StockIds"/> is the capped, distinct selection; <see cref="TotalPending"/>
/// is how many distinct stocks had unreconciled splits before the cap, and
/// <see cref="Skipped"/> is the remainder deferred to a later cycle.
/// </summary>
public record PendingSplitSelection(
    IReadOnlyList<Guid> StockIds,
    int TotalPending,
    int Skipped
);

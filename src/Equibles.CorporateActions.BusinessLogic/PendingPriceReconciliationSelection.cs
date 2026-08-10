namespace Equibles.CorporateActions.BusinessLogic;

/// <summary>
/// The exact listed price series the corporate-action adjustment pass will re-sync this cycle.
/// <see cref="Series"/> is the capped, distinct selection; <see cref="TotalPending"/>
/// is how many distinct series had unreconciled actions before the cap, and
/// <see cref="Skipped"/> is the remainder deferred to a later cycle.
/// </summary>
public sealed record PendingPriceReconciliationSelection(
    IReadOnlyList<PendingPriceReconciliationSeries> Series,
    int TotalPending,
    int Skipped
);

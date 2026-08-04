using Equibles.CorporateActions.Data.Models;

namespace Equibles.CorporateActions.BusinessLogic;

/// <summary>
/// The exact listed price series the split back-adjustment pass will re-sync this cycle.
/// <see cref="Series"/> is the capped, distinct selection; <see cref="TotalPending"/>
/// is how many distinct series had unreconciled splits before the cap, and
/// <see cref="Skipped"/> is the remainder deferred to a later cycle.
/// </summary>
public record PendingSplitSelection(
    IReadOnlyList<PendingSplitSeries> Series,
    int TotalPending,
    int Skipped
);

public sealed record PendingSplitSeries(
    Guid CommonStockId,
    string ListedTicker,
    IReadOnlyList<PendingSplitSnapshot> Splits
);

/// <summary>
/// The immutable split state whose Yahoo history was requested. Stamping compares every value
/// after locking the parent stock, so a split captured or revised during the fetch stays pending.
/// </summary>
public readonly record struct PendingSplitSnapshot(
    Guid Id,
    DateOnly EffectiveDate,
    decimal Numerator,
    decimal Denominator,
    StockSplitSource Source
);

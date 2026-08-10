using Equibles.CorporateActions.Data.Models;

namespace Equibles.CorporateActions.BusinessLogic;

/// <summary>
/// The immutable split state whose provider history was requested.
/// </summary>
public readonly record struct PendingSplitSnapshot(
    Guid Id,
    DateOnly EffectiveDate,
    decimal Numerator,
    decimal Denominator,
    StockSplitSource Source
);

using Equibles.CorporateActions.Data.Models;

namespace Equibles.CorporateActions.BusinessLogic;

/// <summary>
/// The immutable cash-dividend state whose provider history was requested.
/// </summary>
public readonly record struct PendingDividendSnapshot(
    Guid Id,
    DateOnly ExDate,
    decimal AmountPerShare,
    CashDividendSource Source
);

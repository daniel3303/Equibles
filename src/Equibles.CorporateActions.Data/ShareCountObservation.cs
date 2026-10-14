namespace Equibles.CorporateActions.Data;

/// <summary>An issuer's reported common-share count as of a date, and when it was filed.</summary>
public readonly record struct ShareCountObservation(
    DateOnly AsOf,
    DateOnly FiledDate,
    decimal Shares
);

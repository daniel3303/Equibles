namespace Equibles.Holdings.BusinessLogic.Intelligence.Models;

/// <summary>
/// A book's value-weighted tenure in quarters and the share of value whose tenure is censored at
/// the first covered quarter.
/// </summary>
public sealed record HolderTenureSummary(
    decimal? ValueWeightedQuarters,
    decimal? CensoredValueShare
);

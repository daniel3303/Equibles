namespace Equibles.Holdings.BusinessLogic;

/// <summary>
/// The one bound for "a share count priced at a market price grossly disagrees with the value a
/// filer reports": beyond it the two figures are on different units or bases (a missed split is at
/// least 2×, a units error 100× or more), not apart by price drift.
/// </summary>
public static class HoldingValueDisagreement
{
    /// <summary>
    /// Shares × price more than this multiple above the reported value is a basis error. Valuation
    /// publishes the filed figure past it, and holder trades refuse a price past it.
    /// </summary>
    public const decimal CapMultiple = 5m;

    /// <summary>Whether <paramref name="pricedValue"/> grossly exceeds a positive reported value.</summary>
    public static bool GrosslyExceeds(decimal pricedValue, decimal reportedValue) =>
        reportedValue > 0 && pricedValue > CapMultiple * reportedValue;
}

namespace Equibles.Holdings.HostedService.Services;

/// <summary>
/// The unit a 13F information table's VALUE column is filed in.
/// </summary>
/// <remarks>
/// The SEC's 2022 13F modernization made the column whole dollars from
/// <see cref="WholeDollarEffectiveDate"/>; every earlier filing reports thousands. A filed figure
/// is therefore only comparable to a dollar amount derived from shares × price after being scaled
/// by its own filing's era — comparing the two raw invents a 1,000× disagreement on every
/// pre-2023 filing.
/// </remarks>
internal static class FiledValueScale
{
    internal static readonly DateOnly WholeDollarEffectiveDate = new(2023, 1, 3);

    internal static decimal ToDollars(long filedValue, DateOnly filingDate) =>
        filingDate < WholeDollarEffectiveDate ? filedValue * 1000m : filedValue;
}

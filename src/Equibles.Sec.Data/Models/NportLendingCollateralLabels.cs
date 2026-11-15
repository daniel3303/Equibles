using System.Globalization;

namespace Equibles.Sec.Data.Models;

/// <summary>
/// The wording every text surface uses for holdings the report marks as lending collateral
/// (<see cref="NportHolding.IsLendingCollateral"/>), so a row's label and the schedule's total read
/// the same wherever they are printed.
/// </summary>
public static class NportLendingCollateralLabels
{
    /// <summary>Appended to the asset category of a row that is lending collateral.</summary>
    public const string CategorySuffix = ", securities-lending collateral";

    /// <summary>One line stating the collateral rows' count, value and share of net assets.</summary>
    public static string Total(int rows, decimal valueUsd, decimal percentOfNetAssets) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"Securities-lending collateral: {rows} {(rows == 1 ? "row" : "rows")}, ${valueUsd:N2} ({percentOfNetAssets:N2}% of net assets). The report marks {(rows == 1 ? "this row" : "these rows")} as cash received from borrowers of the fund's securities and reinvested, not as positions the fund chose."
        );
}

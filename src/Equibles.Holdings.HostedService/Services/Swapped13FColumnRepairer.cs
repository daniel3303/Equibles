using Equibles.Holdings.BusinessLogic.Intelligence;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService.Models;

namespace Equibles.Holdings.HostedService.Services;

/// <summary>
/// Detects and repairs 13F filings whose share-count and value columns are swapped: the filer's
/// software wrote each position's dollar value into <c>sshPrnamt</c> and its share count into
/// <c>value</c>. Read as filed, a position's dollar value becomes its share count, and every
/// derivation is inflated by the share price.
/// </summary>
/// <remarks>
/// The evidence is the whole filing, never one row: on most priced rows the filed value per share
/// disagrees with the quarter's price while the swapped columns reproduce it. A correctly filed
/// table shows that only for prices near the square root of the value column's unit, so the
/// signature cannot arise from scattered coincidences, and the swapped columns state the units
/// themselves (dollars over shares is the price), so the repair needs no era guess.
/// </remarks>
internal static class Swapped13FColumnRepairer
{
    /// <summary>
    /// True when at least <see cref="Corrupt13FShareCountRepairer.SuspectRowFraction"/> of the
    /// filing's priced common-share rows fit the quarter price only once their columns are swapped.
    /// </summary>
    internal static bool IsSuspect(
        IReadOnlyCollection<BufferedHoldingRow> rows,
        Func<InstitutionalHolding, decimal?> filingBasisPrice
    )
    {
        var comparable = 0;
        var swapped = 0;
        foreach (var row in rows)
        {
            var fit = Classify(row, filingBasisPrice);
            if (fit == Fit.Unpriced)
                continue;
            comparable++;
            if (fit == Fit.Swapped)
                swapped++;
        }

        return comparable >= Corrupt13FShareCountRepairer.MinimumComparableRows
            && swapped >= comparable * Corrupt13FShareCountRepairer.SuspectRowFraction;
    }

    /// <summary>
    /// Swaps the columns back on every share-quantity row of a suspect filing, unpriced and option
    /// rows included, except one whose filed figures alone fit the price; principal rows keep their
    /// filed quantity. <paramref name="revalue"/> re-derives each repaired row. Returns the count.
    /// </summary>
    internal static int Repair(
        List<BufferedHoldingRow> rows,
        Func<InstitutionalHolding, decimal?> filingBasisPrice,
        Action<BufferedHoldingRow> revalue
    )
    {
        var repaired = 0;
        foreach (var row in rows)
        {
            if (!HasBothColumns(row) || Classify(row, filingBasisPrice) == Fit.AsFiled)
                continue;

            // The share column holds whole dollars whatever the filing's era: the detection read
            // dollars over shares as the price.
            var dollars = row.Holding.Shares;
            row.Holding.Shares = row.ReportedValue;
            row.Holding.FiledValue = dollars;
            row.ManagerEntry.Shares = row.ReportedValue;
            revalue(row);
            repaired++;
        }
        return repaired;
    }

    private enum Fit
    {
        Unpriced,
        AsFiled,
        Swapped,
        Both,
        Neither,
    }

    // Option rows value the contract, not the shares, so only common-share rows are priced.
    private static Fit Classify(
        BufferedHoldingRow row,
        Func<InstitutionalHolding, decimal?> filingBasisPrice
    )
    {
        var holding = row.Holding;
        if (
            !HasBothColumns(row)
            || holding.OptionType != null
            || filingBasisPrice(holding) is not { } price
            || price <= 0
        )
            return Fit.Unpriced;

        // A filer that mislabels its value unit (dollars before 2023, thousands after) still fits
        // the price as filed under the other unit, so neither unit fitting is required before a row
        // counts as evidence of swapped columns.
        var filedDollars = FiledValueScale.ToDollars(row.ReportedValue, holding.FilingDate);
        var otherUnitDollars =
            holding.FilingDate < FiledValueScale.WholeDollarEffectiveDate
                ? row.ReportedValue
                : row.ReportedValue * 1000m;
        var asFiled =
            Agrees(filedDollars / holding.Shares, price)
            || Agrees(otherUnitDollars / holding.Shares, price);
        var swapped = Agrees((decimal)holding.Shares / row.ReportedValue, price);
        return (asFiled, swapped) switch
        {
            (true, true) => Fit.Both,
            (true, false) => Fit.AsFiled,
            (false, true) => Fit.Swapped,
            _ => Fit.Neither,
        };
    }

    private static bool HasBothColumns(BufferedHoldingRow row) =>
        row.Holding.ShareType == ShareType.Shares
        && row.Holding.Shares > 0
        && row.ReportedValue > 0;

    // The band within which a filer's own mark and the stored close are the same price.
    private static bool Agrees(decimal perShare, decimal price)
    {
        var ratio = perShare / price;
        return ratio <= QuarterAveragePriceCalculator.MaximumFiledPriceRatio
            && ratio * QuarterAveragePriceCalculator.MaximumFiledPriceRatio >= 1;
    }
}

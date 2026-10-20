using Equibles.Holdings.BusinessLogic.Intelligence;
using Equibles.Holdings.Data.Models;

namespace Equibles.Holdings.BusinessLogic.Signals;

/// <summary>
/// The figures every surface derives from a <see cref="StockQuarterlyHolderSignals"/> row, in one
/// place so MCP, REST, ALVIS, the portal and the screener agree. Ratios are fractions (0.25 is 25%);
/// null means not measurable, never zero.
/// </summary>
public static class StockHolderSignalsMath
{
    /// <summary>The quarter-end price per share the 13F values imply, on the quarter's basis.</summary>
    public static decimal? QuarterEndPrice(StockQuarterlyHolderSignals row) =>
        row is { ValuedShares: > 0, Value: > 0 } ? (decimal)row.Value / row.ValuedShares : null;

    /// <summary>
    /// The quarter-end price over the estimated holder cost, less one: positive when holders are
    /// on average in profit. Null without both.
    /// </summary>
    public static decimal? CostGap(StockQuarterlyHolderSignals row) =>
        QuarterEndPrice(row) is { } price && row.EstimatedCostPerShare is > 0 and var cost
            ? price / cost - 1
            : null;

    /// <summary>The share of 13F shares whose cost could be estimated.</summary>
    public static decimal? CostCoverage(StockQuarterlyHolderSignals row) =>
        row.Shares > 0 ? (decimal)row.PricedShares / row.Shares : null;

    /// <summary>Shares outstanding less insiders' shares, where both divide this listing.</summary>
    public static long? EstimatedFreeFloat(StockQuarterlyHolderSignals row) =>
        StockHolderSignalsCalculator.CountDividesListing(row) && row.InsiderShares is { } insiders
            ? row.SharesOutstanding.Value - insiders
            : null;

    /// <summary>
    /// 13F shares over the estimated free float; above one (more than the float) is possible when
    /// filers report shares already lent out or counted twice, and surfaces flag it.
    /// </summary>
    public static decimal? InstitutionalShareOfFloat(StockQuarterlyHolderSignals row) =>
        EstimatedFreeFloat(row) is > 0 and var floatShares
            ? (decimal)row.Shares / floatShares
            : null;

    /// <summary>13F shares over shares outstanding, where the count divides this listing.</summary>
    public static decimal? InstitutionalShareOfOutstanding(StockQuarterlyHolderSignals row) =>
        StockHolderSignalsCalculator.CountDividesListing(row)
            ? (decimal)row.Shares / row.SharesOutstanding.Value
            : null;

    /// <summary>Crowded shares over shares outstanding, where the count divides this listing.</summary>
    public static decimal? Crowding(StockQuarterlyHolderSignals row) =>
        StockHolderSignalsCalculator.CountDividesListing(row)
            ? (decimal)row.CrowdedShares / row.SharesOutstanding.Value
            : null;

    /// <summary>Private-fund advisers' shares over shares outstanding, where the count divides.</summary>
    public static decimal? PrivateFundOwnership(StockQuarterlyHolderSignals row) =>
        StockHolderSignalsCalculator.CountDividesListing(row)
            ? (decimal)row.PrivateFundAdviserShares / row.SharesOutstanding.Value
            : null;

    /// <summary>Active holders' like-for-like share change over the quarter, as a fraction.</summary>
    public static decimal? ActiveLikeForLikeChange(StockQuarterlyHolderSignals row) =>
        row is { ActiveLikeForLikePreviousShares: > 0, ActiveLikeForLikeShares: { } current }
            ? (decimal)current / row.ActiveLikeForLikePreviousShares.Value - 1
            : null;

    /// <summary>The share of valued 13F holdings by tenure band, short to censored.</summary>
    public static (decimal Short, decimal Medium, decimal Long, decimal Censored)? TenureMix(
        StockQuarterlyHolderSignals row
    )
    {
        var total =
            row.TenureShortValue
            + row.TenureMediumValue
            + row.TenureLongValue
            + row.TenureCensoredValue;
        if (total <= 0)
            return null;
        return (
            (decimal)row.TenureShortValue / total,
            (decimal)row.TenureMediumValue / total,
            (decimal)row.TenureLongValue / total,
            (decimal)row.TenureCensoredValue / total
        );
    }

    /// <summary>The fraction of filers holding the stock among their ten largest issuers.</summary>
    public static decimal? ConvictionShare(StockQuarterlyHolderSignals row) =>
        row.Filers > 0 ? (decimal)row.ConvictionHolders / row.Filers : null;

    /// <summary>Why a percentage of shares outstanding cannot be given for this row; null when it can.</summary>
    public static string OutstandingUnavailableReason(StockQuarterlyHolderSignals row) =>
        row.ListedTicker != null
            ? "shares outstanding is an issuer-wide count, not one listing's"
            : row.SharesOutstandingBasis switch
            {
                SharesOutstandingBasis.None =>
                    "no cover-page share count is dated within about a year of the quarter end",
                SharesOutstandingBasis.ClassSum =>
                    "the issuer has more than one class of common stock, so its count is the total across classes",
                SharesOutstandingBasis.ForeignOrdinary =>
                    "a foreign private issuer reports ordinary shares, a different unit from the listed shares",
                _ => row.SharesOutstanding is > 0 ? null : "no share count is on record",
            };
}

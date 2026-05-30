namespace Equibles.Yahoo.Repositories;

/// <summary>
/// Trailing- and calendar-window price returns, each expressed as a percentage
/// (e.g. <c>12.5</c> means +12.5%). A window is <c>null</c> when the series lacks
/// the history that window needs.
/// </summary>
public class PriceReturns
{
    /// <summary>Return over the last 5 trading days.</summary>
    public decimal? FiveDay { get; set; }

    /// <summary>Return over the last 20 trading days.</summary>
    public decimal? TwentyDay { get; set; }

    /// <summary>Return over the last 120 trading days.</summary>
    public decimal? OneHundredTwentyDay { get; set; }

    /// <summary>Return from the prior month's final close to the latest close.</summary>
    public decimal? MonthToDate { get; set; }

    /// <summary>Return from the prior year's final close to the latest close.</summary>
    public decimal? YearToDate { get; set; }
}

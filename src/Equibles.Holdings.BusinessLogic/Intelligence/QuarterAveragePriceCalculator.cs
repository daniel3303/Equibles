using Equibles.Holdings.BusinessLogic.Intelligence.Models;

namespace Equibles.Holdings.BusinessLogic.Intelligence;

/// <summary>
/// A security's average traded price over a quarter on the quarter-end filing basis, accepted
/// only when the series' quarter-end close agrees with what 13F filers valued the security at.
/// </summary>
/// <remarks>
/// The stored closes carry no basis marker and a later split may or may not have restated them,
/// so the filers' own quarter-end valuation is the only independent check of the basis.
/// </remarks>
public static class QuarterAveragePriceCalculator
{
    /// <summary>About a third of a quarter's sessions.</summary>
    public const int MinimumSessions = 20;

    /// <summary>The last session must fall within a week of the quarter end.</summary>
    public const int MaximumLastSessionGapDays = 7;

    /// <summary>The largest accepted ratio either way between the series and the filed price.</summary>
    public const decimal MaximumFiledPriceRatio = 1.25m;

    /// <summary>The average on the quarter-end filing basis, or null when it cannot be trusted.</summary>
    public static decimal? AveragePrice(QuarterPriceSeries series, DateOnly reportDate)
    {
        if (
            series.Sessions < MinimumSessions
            || series.SplitInsideQuarter
            || series.LastSessionDate < reportDate.AddDays(-MaximumLastSessionGapDays)
            || series.ShareCountFactor is not decimal factor
            || factor <= 0
            || series.FiledPricePerShare is not decimal filed
            || filed <= 0
            || series.LastClose <= 0
            || series.AverageClose <= 0
        )
            return null;

        var ratio = series.LastClose * factor / filed;
        if (ratio > MaximumFiledPriceRatio || ratio * MaximumFiledPriceRatio < 1)
            return null;
        return series.AverageClose * factor;
    }
}

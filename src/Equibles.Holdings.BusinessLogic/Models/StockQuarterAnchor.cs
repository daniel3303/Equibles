namespace Equibles.Holdings.BusinessLogic.Models;

/// <summary>
/// How a stock's newest 13F quarter should be presented, resolved by
/// <see cref="StockCombinedQuarterService"/> so every surface (web, MCP, agents) shows the same
/// picture: the combined view while the filing window is open, the as-filed quarter afterwards.
/// </summary>
public class StockQuarterAnchor
{
    /// <summary>The stock's newest 13F quarter end.</summary>
    public DateOnly ReportDate { get; set; }

    /// <summary>The 13F quarter immediately before <see cref="ReportDate"/>, when one exists.</summary>
    public DateOnly? PreviousReportDate { get; set; }

    /// <summary>True while filings for <see cref="ReportDate"/> are still arriving (45-day window).</summary>
    public bool FilingWindowOpen { get; set; }

    /// <summary>
    /// True when positions must be presented as the combined view: the window is open AND a
    /// previous quarter exists to carry non-filers forward from. An open window with no prior
    /// quarter degenerates to the as-filed rows.
    /// </summary>
    public bool IsCombined => FilingWindowOpen && PreviousReportDate.HasValue;
}

namespace Equibles.Holdings.Repositories.Models;

public class InstitutionPortfolioSummary
{
    public long ReportedAum { get; set; }
    public int PositionCount { get; set; }
    public double Top10ConcentrationPercent { get; set; }
    public double Top25ConcentrationPercent { get; set; }

    // QoQ turnover = (sum of |Δ shares × current price proxy|) / (2 × AUM), expressed as a
    // percent. Uses the current quarter's per-share Value/Shares as the price proxy so no
    // extra price-history dependency is required.
    public double QoQTurnoverPercent { get; set; }

    public int QuartersReported { get; set; }
    public DateOnly? LatestReportDate { get; set; }
    public DateOnly? PreviousReportDate { get; set; }
}

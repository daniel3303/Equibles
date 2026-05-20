namespace Equibles.Holdings.Repositories.Projections;

public class InstitutionPortfolioSummary
{
    public long ReportedAum { get; set; }
    public int PositionCount { get; set; }
    public decimal Top10ConcentrationPercent { get; set; }
    public decimal Top25ConcentrationPercent { get; set; }
    public decimal? QuarterOverQuarterTurnoverPercent { get; set; }
    public int QuartersReported { get; set; }
    public DateOnly? LatestReportDate { get; set; }
    public DateOnly? PreviousReportDate { get; set; }
}

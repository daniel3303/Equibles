namespace Equibles.Holdings.Repositories.Models;

public class BacktestQuarterSnapshot
{
    public DateOnly ReportDate { get; set; }

    public List<BacktestPosition> Positions { get; set; } = [];
}

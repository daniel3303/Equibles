namespace Equibles.Holdings.Repositories.Models;

public class BacktestResult
{
    public DateOnly StartDate { get; set; }

    public DateOnly EndDate { get; set; }

    public List<BacktestPoint> Points { get; set; } = [];

    public BacktestStrategySummary PortfolioSummary { get; set; } = new();

    public BacktestStrategySummary BenchmarkSummary { get; set; } = new();

    public string Reason { get; set; }
}

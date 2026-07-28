namespace Equibles.Holdings.Repositories.Models;

public class BacktestResult
{
    public DateOnly StartDate { get; set; }

    public DateOnly EndDate { get; set; }

    public List<BacktestPoint> Points { get; set; } = [];

    public BacktestStrategySummary PortfolioSummary { get; set; } = new();

    public BacktestStrategySummary BenchmarkSummary { get; set; } = new();

    public string Reason { get; set; }

    /// <summary>
    /// How much of the filer's reported book the simulation actually tracked. Null when nothing was
    /// simulated. A surface that shows the return must show this too — see
    /// <see cref="BacktestCoverage"/>.
    /// </summary>
    public BacktestCoverage Coverage { get; set; }

    /// <summary>
    /// Set when the requested window ran past the point where the filer's portfolio stopped being
    /// current and the simulation was cut short there. Null when the window was honoured in full.
    /// </summary>
    public DateOnly? TruncatedAt { get; set; }
}

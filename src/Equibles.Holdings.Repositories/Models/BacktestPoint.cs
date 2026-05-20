namespace Equibles.Holdings.Repositories.Models;

public class BacktestPoint
{
    public DateOnly Date { get; set; }

    public decimal PortfolioValue { get; set; }

    public decimal BenchmarkValue { get; set; }
}

namespace Equibles.Holdings.Repositories.Models;

public class BacktestStrategySummary
{
    public decimal TotalReturnPercent { get; set; }

    // Null when the simulated window is shorter than
    // HoldingsBacktestCalculator.MinAnnualizationDays — too short to annualize meaningfully.
    public decimal? CagrPercent { get; set; }

    public decimal MaxDrawdownPercent { get; set; }
}

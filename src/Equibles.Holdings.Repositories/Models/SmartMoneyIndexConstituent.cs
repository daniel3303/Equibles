namespace Equibles.Holdings.Repositories.Models;

/// <summary>
/// One stock selected into a smart-money index: a high-conviction holding shared across the
/// top-scoring funds. <see cref="Ticker"/> and <see cref="Name"/> are left null by
/// <see cref="SmartMoneyIndexCalculator"/> (which works in stock ids) and filled in by the
/// orchestrating manager for display.
/// </summary>
public class SmartMoneyIndexConstituent
{
    public Guid CommonStockId { get; set; }

    public string Ticker { get; set; }

    public string Name { get; set; }

    /// <summary>How many of the top-scoring funds held this stock in their latest 13F.</summary>
    public int HeldByCount { get; set; }

    /// <summary>
    /// Mean portfolio weight (as a percentage) of this stock across the funds that hold it —
    /// the conviction signal the selection ranks on after consensus count.
    /// </summary>
    public decimal AverageWeightPercent { get; set; }

    /// <summary>This stock's weight in the equal-weighted index (100 / constituent count).</summary>
    public decimal IndexWeightPercent { get; set; }
}

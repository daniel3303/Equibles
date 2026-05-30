using Equibles.Holdings.Repositories.Models;

namespace Equibles.Holdings.BusinessLogic.Models;

/// <summary>
/// The constructed smart-money index: the equal-weighted basket of the top-scoring funds'
/// highest-conviction common holdings, plus the forward backtest of that basket against the
/// benchmark. When the index can't be built (no scored funds, no consensus stocks, no prices),
/// <see cref="Constituents"/> is empty and <see cref="Reason"/> explains why.
/// </summary>
public class SmartMoneyIndexResult
{
    /// <summary>Number of top-alpha funds requested for the consensus.</summary>
    public int RequestedTopFunds { get; set; }

    /// <summary>Number of top-alpha funds actually found and used.</summary>
    public int FundCount { get; set; }

    /// <summary>Cap applied to the number of constituents.</summary>
    public int MaxConstituents { get; set; }

    /// <summary>Minimum number of funds that had to hold a stock for it to qualify.</summary>
    public int MinConsensus { get; set; }

    /// <summary>Rolling-window length (years) the funds were ranked by alpha over.</summary>
    public int WindowYears { get; set; }

    public string BenchmarkTicker { get; set; }

    public string BenchmarkName { get; set; }

    /// <summary>Last day of the tracked window — the as-of date the index was evaluated on.</summary>
    public DateOnly AsOf { get; set; }

    /// <summary>
    /// Freshest 13F report date among the selected funds — the quarter the basket reflects.
    /// Forward tracking starts 45 days after this (the latest legal filing deadline).
    /// </summary>
    public DateOnly? ConstructionDate { get; set; }

    public List<SmartMoneyIndexConstituent> Constituents { get; set; } = [];

    public BacktestResult Backtest { get; set; } = new();

    /// <summary>Set when the index is empty or the backtest could not run; null on success.</summary>
    public string Reason { get; set; }
}

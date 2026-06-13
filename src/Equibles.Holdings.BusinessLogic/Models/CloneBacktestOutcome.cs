using Equibles.Holdings.Repositories.Models;

namespace Equibles.Holdings.BusinessLogic.Models;

/// <summary>
/// Result of an on-demand single-filer clone backtest: the resolved filer and benchmark, the
/// window actually simulated, and the clone-vs-benchmark <see cref="BacktestResult"/>. Host-neutral
/// so both the web profile page and the MCP tool can map it to their own shapes.
/// </summary>
public class CloneBacktestOutcome
{
    public string Cik { get; set; }

    public string HolderName { get; set; }

    public string Benchmark { get; set; }

    public string BenchmarkName { get; set; }

    public DateOnly? RequestedFrom { get; set; }

    public DateOnly? RequestedTo { get; set; }

    // The window the simulation actually ran over once defaults were resolved (RequestedFrom/To
    // may be null; the provider fills them from the earliest snapshot's rebalance date and today).
    public DateOnly ResolvedFrom { get; set; }

    public DateOnly ResolvedTo { get; set; }

    public BacktestResult Result { get; set; } = new();

    public bool HolderNotFound { get; set; }

    public bool BenchmarkNotFound { get; set; }
}

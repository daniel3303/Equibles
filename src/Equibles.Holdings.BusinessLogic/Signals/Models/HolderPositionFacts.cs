using Equibles.Holdings.BusinessLogic.Intelligence.Models;

namespace Equibles.Holdings.BusinessLogic.Signals.Models;

/// <summary>
/// How long a filer has held one listing and its estimated cost per share, at a quarter end on that
/// quarter's share basis. A null <see cref="CostStatus"/> means the quarter is not computed yet.
/// </summary>
public sealed record HolderPositionFacts
{
    /// <summary>Consecutive quarters held through the quarter; null when not measured.</summary>
    public int? HeldQuarters { get; init; }

    /// <summary>True when the run reaches the corpus start, so it is at least that long.</summary>
    public bool HeldSinceCoverageStart { get; init; }

    public HolderCostStatus? CostStatus { get; init; }
    public decimal? EstimatedCostPerShare { get; init; }
}

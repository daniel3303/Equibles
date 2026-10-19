namespace Equibles.Holdings.BusinessLogic.Intelligence.Models;

/// <summary>One closed quarter's holder flows by the buckets of one dimension.</summary>
public sealed record HolderFlowQuarter
{
    public DateOnly ReportDate { get; init; }
    public HolderMixDimension Dimension { get; init; }
    public HolderFlowGrouping Grouping { get; init; }

    /// <summary>False until the quarter is recomputed with per-sector trades.</summary>
    public bool SectorSplitAvailable { get; init; }

    public DateTime ComputedAt { get; init; }
    public IReadOnlyList<HolderFlowBucket> Buckets { get; init; } = [];
    public HolderFlowBucket Total { get; init; }
}

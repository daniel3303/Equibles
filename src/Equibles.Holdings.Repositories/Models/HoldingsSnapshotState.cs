namespace Equibles.Holdings.Repositories.Models;

public sealed record HoldingsSnapshotState(bool IsDirty, DateTime? ComputedAt)
{
    public bool CanCache => !IsDirty && ComputedAt.HasValue;
}

public sealed record HolderSummarySnapshotState(
    bool IsDirty,
    DateTime? CurrentComputedAt,
    DateTime? PreviousComputedAt
)
{
    public bool CanCache(bool hasPreviousQuarter) =>
        !IsDirty
        && CurrentComputedAt.HasValue
        && (!hasPreviousQuarter || PreviousComputedAt.HasValue);
}

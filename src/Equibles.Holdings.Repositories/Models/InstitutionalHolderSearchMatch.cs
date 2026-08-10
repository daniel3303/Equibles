using Equibles.Holdings.Data.Models;

namespace Equibles.Holdings.Repositories.Models;

public sealed class InstitutionalHolderSearchMatch
{
    public required InstitutionalHolder Holder { get; init; }
    public DateOnly? LatestReportDate { get; init; }
    public long? ReportedAum { get; init; }
    public int? PositionCount { get; init; }
}

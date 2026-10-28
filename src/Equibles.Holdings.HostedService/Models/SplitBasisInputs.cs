namespace Equibles.Holdings.HostedService.Models;

/// <summary>
/// What the split-basis repair of one filing needs from stored data: the positions whose quarter
/// confirms its price basis, and each such position's neighbouring quarter on its report-date
/// basis, both keyed like the upsert. A position missing from the first is never judged.
/// </summary>
internal sealed record SplitBasisInputs(
    IReadOnlyDictionary<string, long> Neighbours,
    IReadOnlySet<string> CorroboratedPositions
)
{
    public static readonly SplitBasisInputs None = new(
        new Dictionary<string, long>(),
        new HashSet<string>()
    );
}

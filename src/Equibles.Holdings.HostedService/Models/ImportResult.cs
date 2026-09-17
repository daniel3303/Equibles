namespace Equibles.Holdings.HostedService.Models;

/// <summary>
/// Outcome of one <see cref="Services.HoldingsImportService.ImportDataSet"/> call.
/// <paramref name="InsertedHoldings"/> is the number of holding rows the import
/// actually upserted — the real-time path treats a non-amendment original that
/// imported as "complete" yet inserted zero holdings as suspect (it does NOT
/// record it processed, so a later cycle retries) rather than silently
/// consuming the filing forever.
/// <paramref name="ConflictedFilings"/> lists the accessions skipped because their positions
/// could not be told apart under their retained observation identities. The import still
/// completes — the conflict is a stored-data defect that no retry can clear — so the caller has
/// to surface these, or the skip is invisible.
/// </summary>
public record ImportResult(
    int SubmissionCount,
    bool IsComplete,
    int InsertedHoldings = 0,
    IReadOnlyList<string> ConflictedFilings = null
)
{
    public IReadOnlyList<string> ConflictedFilings { get; init; } = ConflictedFilings ?? [];
}

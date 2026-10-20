using Equibles.Holdings.Data.Models;

namespace Equibles.Holdings.HostedService.Models;

/// <summary>
/// Outcome of one recovery pass over a manager's filing tail. <paramref name="Resolvable"/> lists
/// the pending accessions that imported with every later attempt; <paramref name="Complete"/> means
/// every attempt imported, so the manager's whole pending set is settled.
/// </summary>
public record RecoveryReplayResult(
    int Attempted,
    int Imported,
    int Skipped,
    IReadOnlyList<string> Resolvable,
    bool Complete
)
{
    /// <summary>
    /// The resolvable accessions that completed without storing a row, with the source proof that
    /// there was nothing to store. A resolvable accession absent here went through the holdings
    /// write.
    /// </summary>
    public IReadOnlyDictionary<
        string,
        HoldingsImportFailureReason
    > ClosedWithoutRows { get; init; } = new Dictionary<string, HoldingsImportFailureReason>();
}

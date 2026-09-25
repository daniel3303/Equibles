namespace Equibles.Congress.HostedService.Models;

/// <summary>
/// A source filing a client fully handled this cycle: fetched, parsed and
/// either yielded items or was skipped by a deterministic policy. The sync
/// services persist these as <c>CongressionalFilingRecord</c> rows — but only
/// after the cycle's data has been committed, so a failed persist re-fetches
/// the filing instead of losing it. <c>RejectedRowCount</c> counts transaction-shaped rows the
/// parser could not read: the filing is still recorded (the verdict is deterministic), so the
/// sync service reports it once where it stays queryable until a parser bump reopens it.
/// </summary>
public sealed record ProcessedFiling(
    string SourceId,
    DateOnly FilingDate,
    int ItemCount,
    int RejectedRowCount = 0
);

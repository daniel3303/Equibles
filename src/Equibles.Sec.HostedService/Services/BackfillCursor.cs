namespace Equibles.Sec.HostedService.Services;

/// <summary>
/// Tracks how far a CreationTime-ordered backfill has progressed so each poll seeks straight
/// to the frontier instead of anti-joining the whole table. New rows are always created at the
/// frontier (CreationTime = insert time), so a floored batch query sees all new work; rows left
/// behind the frontier (an item that failed to process, or work re-queued by deleting its
/// output) are caught by a rate-limited full rescan. The cursor is owned by the long-lived
/// worker — the per-scope manager only reads and advances it — so it survives DI scopes and
/// resets on process restart, which itself forces a fresh full scan.
/// </summary>
public class BackfillCursor
{
    // A full (unfloored) scan anti-joins the entire table — minutes on the chunk corpus — so
    // the drained-frontier fallback is allowed at most once per interval. One hour keeps the
    // straggler-retry latency bounded without letting the scan dominate idle DB load again.
    private static readonly TimeSpan FullRescanInterval = TimeSpan.FromHours(1);

    private DateTime _lastFullScanUtc = DateTime.MinValue;

    /// <summary>
    /// Lower bound for the next batch query, or null when the next query must scan from the
    /// start. Batches are CreationTime-ordered, so queries use an inclusive floor: rows that
    /// share the frontier timestamp are re-read, and the ones already processed drop out via
    /// the query's own not-yet-processed predicate.
    /// </summary>
    public DateTime? Floor { get; private set; }

    /// <summary>Moves the frontier to the newest CreationTime the current batch reached.</summary>
    public void Advance(DateTime lastBatchCreationTime)
    {
        Floor = lastBatchCreationTime;
    }

    /// <summary>
    /// Rate-limits the unfloored fallback scan to one per rescan interval. Returns false when
    /// still rate-limited — the caller should treat the backfill as drained for this poll. The
    /// floor itself is left untouched: an empty rescan must not discard the frontier, or new
    /// rows arriving right after it would wait out the full interval instead of being picked
    /// up by the next floored poll. A rescan that does find stragglers moves the floor back
    /// via Advance, and the floored path then works itself forward again.
    /// </summary>
    public bool TryStartFullRescan(DateTime utcNow)
    {
        if (utcNow - _lastFullScanUtc < FullRescanInterval)
            return false;

        _lastFullScanUtc = utcNow;
        return true;
    }
}

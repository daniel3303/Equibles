namespace Equibles.Sec.HostedService.Services;

/// <summary>
/// Tracks how far a CreationTime-ordered backfill has progressed so each poll seeks straight
/// to the frontier instead of anti-joining the whole table. New rows are always created at the
/// frontier (CreationTime = insert time), so a floored batch query sees all new work. Rows left
/// behind the frontier are caught by two rescan tiers: an hourly rescan bounded to
/// <see cref="BoundedRescanLookback"/> behind the floor (stragglers are read-skew commits or
/// partially processed batches, always near the frontier — an index-range anti-join, not a
/// corpus scan), and an unfloored full scan at most once per day as the backstop for re-queued
/// work older than the bounded window. The cursor is owned by the long-lived worker — the
/// per-scope manager only reads and advances it — and hydrates its floor and full-rescan stamp
/// from the persisted BackfillState row, so a process restart resumes at the frontier instead
/// of paying the minutes-long corpus scan on every boot (deploy bursts used to pay it several
/// times in a row).
/// </summary>
public class BackfillCursor
{
    // Stragglers surface close behind the frontier: a long transaction committing rows whose
    // CreationTime predates an already-advanced floor, or a batch whose processing partially
    // skipped items. An hourly rescan floored a week back catches both through the
    // CreationTime index without touching the rest of the corpus.
    private static readonly TimeSpan BoundedRescanInterval = TimeSpan.FromHours(1);
    public static readonly TimeSpan BoundedRescanLookback = TimeSpan.FromDays(7);

    // A full (unfloored) scan anti-joins the entire table — minutes on the chunk corpus — so it
    // runs at most daily, purely as the backstop for work re-queued behind the bounded window
    // (output deleted to force re-processing). The stamp is persisted via BackfillState, so
    // deploy bursts neither repeat the scan per boot nor dodge it indefinitely.
    private static readonly TimeSpan FullRescanInterval = TimeSpan.FromDays(1);

    // Retry spacing after a full rescan that started but failed (query timeout, interrupted
    // process). Rows older than the bounded window are reachable ONLY by the full rescan, and
    // its stamp is written before the scan runs — so charging a failed scan the full daily
    // interval starves those rows for a day per failure, indefinitely under a recurring
    // timeout. Long enough to not crash-loop a minutes-long scan, far shorter than a day.
    private static readonly TimeSpan FailedFullRescanRetryInterval = TimeSpan.FromMinutes(30);

    private DateTime _lastBoundedScanUtc = DateTime.MinValue;

    /// <summary>The BackfillState row key this cursor hydrates from and persists to.</summary>
    public string Name { get; }

    /// <summary>True once <see cref="Hydrate"/> ran; the manager hydrates on first use per process.</summary>
    public bool IsHydrated { get; private set; }

    /// <summary>
    /// Lower bound for the next batch query, or null when the next query must scan from the
    /// start. Batches are CreationTime-ordered, so queries use an inclusive floor: rows that
    /// share the frontier timestamp are re-read, and the ones already processed drop out via
    /// the query's own not-yet-processed predicate.
    /// </summary>
    public DateTime? Floor { get; private set; }

    /// <summary>
    /// When the last unfloored full rescan started (UTC); null until the first one runs, which
    /// admits the rescan immediately — a fresh install backfills without waiting out a day.
    /// </summary>
    public DateTime? LastFullRescanAt { get; private set; }

    public BackfillCursor(string name)
    {
        Name = name;
    }

    /// <summary>
    /// Seeds the floor and full-rescan stamp from the persisted BackfillState row. The first
    /// call wins; later calls are ignored so live cursor state is never regressed by a stale
    /// re-read.
    /// </summary>
    public void Hydrate(DateTime? floor, DateTime? lastFullRescanAt)
    {
        if (IsHydrated)
            return;

        IsHydrated = true;
        Floor = floor;
        LastFullRescanAt = lastFullRescanAt;
    }

    /// <summary>Moves the frontier to the newest CreationTime the current batch reached.</summary>
    public void Advance(DateTime lastBatchCreationTime)
    {
        Floor = lastBatchCreationTime;
    }

    /// <summary>
    /// Rate-limits the bounded straggler rescan (a query floored at Floor −
    /// <see cref="BoundedRescanLookback"/>) to one per interval. False when still rate-limited
    /// or when there is no floor to bound from — a floorless cursor has never processed
    /// anything, so only the full scan applies. The floor itself is left untouched: a rescan
    /// that finds stragglers moves it back via <see cref="Advance"/>, and the floored path then
    /// works itself forward again.
    /// </summary>
    public bool TryStartBoundedRescan(DateTime utcNow)
    {
        if (Floor is null)
            return false;

        if (utcNow - _lastBoundedScanUtc < BoundedRescanInterval)
            return false;

        _lastBoundedScanUtc = utcNow;
        return true;
    }

    /// <summary>
    /// Rate-limits the unfloored fallback scan to one per rescan interval. Returns false when
    /// still rate-limited — the caller should treat the backfill as drained for this poll. The
    /// floor is left untouched: an empty rescan must not discard the frontier, or new rows
    /// arriving right after it would wait out the full interval instead of being picked up by
    /// the next floored poll.
    /// </summary>
    public bool TryStartFullRescan(DateTime utcNow)
    {
        if (LastFullRescanAt is { } last && utcNow - last < FullRescanInterval)
            return false;

        LastFullRescanAt = utcNow;
        return true;
    }

    /// <summary>
    /// Rewinds the full-rescan stamp after a scan that was admitted but failed, so the next
    /// attempt is admitted <see cref="FailedFullRescanRetryInterval"/> from <paramref name="utcNow"/>
    /// instead of a full rescan interval later. The floor is left untouched.
    /// </summary>
    public void MarkFullRescanFailed(DateTime utcNow)
    {
        LastFullRescanAt = utcNow - FullRescanInterval + FailedFullRescanRetryInterval;
    }
}

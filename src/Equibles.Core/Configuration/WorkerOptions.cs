namespace Equibles.Core.Configuration;

public class WorkerOptions
{
    public DateTime? MinSyncDate { get; set; }
    public List<string> TickersToSync { get; set; } = [];

    /// <summary>
    /// Pause after each quarterly 13F bulk-import write batch. The importer releases its database
    /// scope before waiting, giving request traffic a bounded window between sustained replay
    /// transactions. Realtime filing ingestion does not use this pause.
    /// </summary>
    public int HoldingsBulkBatchPauseMilliseconds { get; set; }

    /// <summary>
    /// Pause after each batch the insider filing reprocess commits. A parser-version bump re-reads
    /// every stored filing back to back, so the pause leaves the database room for other writers.
    /// </summary>
    public int InsiderReprocessBatchPauseMilliseconds { get; set; }

    /// <summary>
    /// Caps how many listed series the corporate-action adjustment pass re-syncs per cycle, so the
    /// one-time universe backfill throttles against Yahoo's shared request limiter instead of
    /// re-pulling every series' full history at once. Series beyond the cap stay pending and are
    /// picked up on later cycles.
    /// </summary>
    public int MaxCorporateActionPriceReconciliationsPerCycle { get; set; } = 50;
}

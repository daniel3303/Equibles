namespace Equibles.Core.Configuration;

public class WorkerOptions
{
    public DateTime? MinSyncDate { get; set; }
    public List<string> TickersToSync { get; set; } = [];

    /// <summary>
    /// Serializes each scraper lane across worker instances with a PostgreSQL advisory lock.
    /// Disable only for hosts that deliberately provide their own lane coordination.
    /// </summary>
    public bool LaneLeaseEnabled { get; set; } = true;

    /// <summary>
    /// Caps how many stocks the split-price back-adjustment pass re-syncs per cycle, so the
    /// one-time universe backfill throttles against Yahoo's shared request limiter instead of
    /// re-pulling every stock's full history at once. Stocks beyond the cap stay pending and are
    /// picked up on later cycles.
    /// </summary>
    public int MaxSplitPriceReconciliationsPerCycle { get; set; } = 50;
}

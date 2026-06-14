namespace Equibles.Sec.HostedService.Configuration;

/// <summary>
/// Controls whether the raw XBRL envelope is captured onto each <c>Document</c> at
/// filing-ingest time. On by default: forward capture reads the envelope from the
/// submission already fetched for ingest, so it adds no extra EDGAR round-trip. An
/// operator can still disable it where the storage-sizing implications aren't wanted.
/// </summary>
public class XbrlCaptureOptions
{
    /// <summary>Master switch — no capture (forward or backfill) happens unless this is true.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// When true (and <see cref="Enabled"/> is true), a background worker walks documents
    /// ingested before capture and fills in their XBRL envelope. Off by default — the
    /// historical sweep is a one-time operation that re-fetches each pending filing's
    /// submission and so spends the shared EDGAR budget; opt in for a deliberate
    /// re-sweep (e.g. after a capture/extraction change) via
    /// <c>XbrlCapture__BackfillEnabled=true</c>.
    /// </summary>
    public bool BackfillEnabled { get; set; } = false;

    /// <summary>How many pending documents the backfill processes per cycle.</summary>
    public int BackfillBatchSize { get; set; } = 128;

    /// <summary>
    /// Seconds to wait between back-to-back cycles while a backlog is still draining (a
    /// cycle that filled its batch). Short so a large historical sweep clears in days, not
    /// weeks; the gap still yields the shared EDGAR budget to the live scrapers between
    /// bursts. Once the queue is drained a cycle falls back to the full 5-minute idle.
    /// </summary>
    public int BackfillDrainIntervalSeconds { get; set; } = 30;
}

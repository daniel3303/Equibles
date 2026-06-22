namespace Equibles.Sec.HostedService.Configuration;

/// <summary>
/// Controls building the stitched "as-filed" HTML (the primary document + its displayable
/// exhibits) onto each <c>Document</c>. Forward capture at ingest reads the submission already
/// fetched for the filing, so it adds no extra EDGAR round-trip; the historical backfill
/// re-fetches each pending filing and so spends the shared EDGAR budget — off by default.
/// </summary>
public class AsFiledHtmlCaptureOptions
{
    /// <summary>Master switch — no as-filed HTML is built (forward or backfill) unless this is true.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// When true (and <see cref="Enabled"/> is true), a background worker walks 8-K documents
    /// whose stitched HTML is missing or stale (below the builder version) and builds it. Off by
    /// default — the historical sweep re-fetches each pending filing's submission, so opt in for a
    /// deliberate re-sweep via <c>AsFiledHtml__BackfillEnabled=true</c>.
    /// </summary>
    public bool BackfillEnabled { get; set; } = false;

    /// <summary>How many pending documents the backfill processes per cycle.</summary>
    public int BackfillBatchSize { get; set; } = 128;

    /// <summary>
    /// Seconds to wait between back-to-back cycles while a backlog is still draining (a cycle
    /// that filled its batch). Short so a large sweep clears in days; the gap still yields the
    /// shared EDGAR budget to the live scrapers between bursts.
    /// </summary>
    public int BackfillDrainIntervalSeconds { get; set; } = 30;
}

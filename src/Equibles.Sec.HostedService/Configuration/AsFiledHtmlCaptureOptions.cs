namespace Equibles.Sec.HostedService.Configuration;

/// <summary>
/// Controls building the stitched "as-filed" HTML (the primary document + its displayable
/// exhibits) onto each <c>Document</c>. Forward capture at ingest reads the submission already
/// fetched for the filing, so it adds no extra EDGAR round-trip; the backfill of older 8-Ks runs
/// by default and self-drains (each is stamped to the current builder version once stitched, so
/// the work-set empties and the worker idles). <see cref="Enabled"/> is the only on/off switch
/// and defaults on — it works out of the box with no configuration.
/// </summary>
public class AsFiledHtmlCaptureOptions
{
    /// <summary>Master switch — no as-filed HTML is built (forward or backfill) unless this is true.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>How many pending documents the backfill processes per cycle.</summary>
    public int BackfillBatchSize { get; set; } = 128;

    /// <summary>
    /// Seconds to wait between back-to-back cycles while a backlog is still draining (a cycle
    /// that filled its batch). Short so a large sweep clears in days; the gap still yields the
    /// shared EDGAR budget to the live scrapers between bursts.
    /// </summary>
    public int BackfillDrainIntervalSeconds { get; set; } = 30;
}

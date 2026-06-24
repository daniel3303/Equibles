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

    /// <summary>
    /// Largest single image (in bytes) downloaded and stored for a filing's as-filed HTML. An
    /// image over this is skipped (the viewer drops the broken reference) so one pathological asset
    /// can't bloat storage. Real EDGAR slide JPGs run well under a megabyte.
    /// </summary>
    public long MaxImageBytes { get; set; } = 8L * 1024 * 1024;

    /// <summary>
    /// Total bytes of images stored for a single document. Once a filing's downloaded images reach
    /// this, the rest are skipped — bounds the storage a single image-heavy deck can consume.
    /// </summary>
    public long MaxTotalImageBytes { get; set; } = 40L * 1024 * 1024;

    /// <summary>
    /// Most images stored for a single document. Bounds the EDGAR fetches and rows one filing can
    /// generate; a long investor deck (~40 slides) sits well under this.
    /// </summary>
    public int MaxImagesPerDocument { get; set; } = 200;
}

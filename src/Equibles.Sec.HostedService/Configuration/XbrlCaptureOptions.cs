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
    /// ingested before capture and fills in their XBRL envelope. Separate from forward
    /// capture so an operator can disable the historical sweep — which re-fetches each
    /// pending filing's submission and so spends the shared EDGAR budget — independently.
    /// </summary>
    public bool BackfillEnabled { get; set; } = true;

    /// <summary>How many pending documents the backfill processes per cycle.</summary>
    public int BackfillBatchSize { get; set; } = 32;
}

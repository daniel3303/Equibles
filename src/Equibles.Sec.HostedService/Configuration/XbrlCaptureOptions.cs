namespace Equibles.Sec.HostedService.Configuration;

/// <summary>
/// Controls whether the raw XBRL envelope is captured onto each <c>Document</c> at
/// filing-ingest time. Off by default: capture only runs once an operator has reviewed
/// the storage-sizing implications and explicitly enables it. The envelope is read from
/// the submission already fetched for ingest, so capture adds no extra EDGAR round-trip.
/// </summary>
public class XbrlCaptureOptions
{
    /// <summary>Master switch — no capture (forward or backfill) happens unless this is true.</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// When true (and <see cref="Enabled"/> is true), a background worker walks documents
    /// ingested before capture and fills in their XBRL envelope. Separate from forward
    /// capture so an operator can enable live capture first and opt into the historical
    /// sweep — which re-fetches each pending filing's submission — deliberately.
    /// </summary>
    public bool BackfillEnabled { get; set; }

    /// <summary>How many pending documents the backfill processes per cycle.</summary>
    public int BackfillBatchSize { get; set; } = 100;
}

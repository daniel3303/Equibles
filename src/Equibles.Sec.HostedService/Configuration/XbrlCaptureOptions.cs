namespace Equibles.Sec.HostedService.Configuration;

/// <summary>
/// Controls whether the raw XBRL envelope is captured onto each <c>Document</c> at
/// filing-ingest time. Off by default: capture only runs once an operator has reviewed
/// the storage-sizing implications and explicitly enables it. The envelope is read from
/// the submission already fetched for ingest, so capture adds no extra EDGAR round-trip.
/// </summary>
public class XbrlCaptureOptions
{
    /// <summary>Master switch — no capture happens unless this is true.</summary>
    public bool Enabled { get; set; }
}

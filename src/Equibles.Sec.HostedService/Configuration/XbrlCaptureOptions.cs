namespace Equibles.Sec.HostedService.Configuration;

/// <summary>
/// Controls whether the raw XBRL envelope is captured onto each <c>Document</c> at
/// filing-ingest time. On by default: forward capture reads the envelope from the
/// submission already fetched for ingest, so it adds no extra EDGAR round-trip. An
/// operator can still disable it where the storage-sizing implications aren't wanted.
/// </summary>
public class XbrlCaptureOptions
{
    /// <summary>Master switch — no capture happens unless this is true.</summary>
    public bool Enabled { get; set; } = true;
}

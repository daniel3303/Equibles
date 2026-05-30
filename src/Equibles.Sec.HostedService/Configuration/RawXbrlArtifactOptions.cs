namespace Equibles.Sec.HostedService.Configuration;

/// <summary>
/// Controls whether raw XBRL envelopes are persisted at filing-ingest time. Off by
/// default: capture only runs once an operator has reviewed the storage-sizing
/// implications and explicitly enables it. <see cref="Enabled"/> is the master
/// switch; the two per-kind flags then select which envelopes to keep.
/// </summary>
public class RawXbrlArtifactOptions
{
    /// <summary>Master switch — no capture happens unless this is true.</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Persist the inline iXBRL primary document (captured before the HTML normalizer
    /// strips <c>ix:header</c>).
    /// </summary>
    public bool CaptureInlineIxbrl { get; set; }

    /// <summary>
    /// Fetch and persist the standalone XBRL <c>.xml</c> instance from the filing's
    /// artifact list, when one is present.
    /// </summary>
    public bool CaptureStandaloneXbrl { get; set; }
}

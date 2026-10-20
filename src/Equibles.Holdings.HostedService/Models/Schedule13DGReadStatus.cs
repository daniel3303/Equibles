namespace Equibles.Holdings.HostedService.Models;

/// <summary>How reading one 13D/13G primary document from EDGAR ended.</summary>
public enum Schedule13DGReadStatus
{
    /// <summary>The document was read and parsed.</summary>
    Parsed,

    /// <summary>EDGAR answered 404 or 410, or served an empty document; it never changes.</summary>
    NotServed,

    /// <summary>
    /// The document could not be parsed, or EDGAR refused it for another reason; a later parser or
    /// coverage version may read it, so nothing marks it unreadable.
    /// </summary>
    Failed,

    /// <summary>EDGAR is unavailable; the pass stops and retries later.</summary>
    Outage,

    /// <summary>
    /// A pre-XML document was read but not reliably; it is parked under the text reader's version
    /// until a later version reads it again.
    /// </summary>
    Refused,
}

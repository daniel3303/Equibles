using Equibles.Sec.Data.Models;

namespace Equibles.Sec.HostedService.Models;

/// <summary>
/// The outcome of examining a filing for its raw XBRL envelope, handed to the persistence
/// layer to apply onto the <c>Document</c>. <see cref="RawBytes"/> is the uncompressed
/// envelope; the persistence layer gzips it. The factory members map one-to-one onto the
/// <see cref="XbrlCaptureStatus"/> values.
/// </summary>
public record XbrlCaptureResult(
    XbrlCaptureStatus Status,
    XbrlType? Type = null,
    string SourceFileName = null,
    byte[] RawBytes = null
)
{
    /// <summary>The filing was not examined (capture disabled, or extraction errored) — left for backfill.</summary>
    public static readonly XbrlCaptureResult NotChecked = new(XbrlCaptureStatus.NotChecked);

    /// <summary>The filing was examined and carries no XBRL envelope.</summary>
    public static readonly XbrlCaptureResult NotPresent = new(XbrlCaptureStatus.NotPresent);

    /// <summary>An XBRL envelope was extracted and should be stored.</summary>
    public static XbrlCaptureResult Captured(
        XbrlType type,
        string sourceFileName,
        byte[] rawBytes
    ) => new(XbrlCaptureStatus.Captured, type, sourceFileName, rawBytes);
}

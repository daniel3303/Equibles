namespace Equibles.Sec.HostedService.Models;

/// <summary>
/// Outcome of building a filing's stitched as-filed HTML (the primary document + its
/// displayable exhibits), handed to the persistence layer. <see cref="Html"/> is the
/// uncompressed page (the persistence layer gzips it); null means the filing was examined but
/// carries no displayable exhibit — there is nothing to stitch, recorded terminally by the
/// version stamp so the backfill won't re-fetch it.
/// </summary>
public record AsFiledHtmlCaptureResult(byte[] Html)
{
    /// <summary>The filing carries no displayable exhibit (or capture is disabled) — nothing to stitch.</summary>
    public static readonly AsFiledHtmlCaptureResult None = new((byte[])null);

    /// <summary>A stitched as-filed page was built and should be stored.</summary>
    public static AsFiledHtmlCaptureResult Built(byte[] html) => new(html);
}

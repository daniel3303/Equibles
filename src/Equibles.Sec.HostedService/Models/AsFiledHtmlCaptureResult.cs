namespace Equibles.Sec.HostedService.Models;

/// <summary>
/// Outcome of building a filing's stitched as-filed HTML (the primary document + its
/// displayable exhibits), handed to the persistence layer. <see cref="Html"/> is the
/// uncompressed page (the persistence layer gzips it); null means the filing was examined but
/// carries no displayable exhibit — there is nothing to stitch, recorded terminally by the
/// version stamp so the backfill won't re-fetch it. <see cref="Images"/> are the filing's
/// referenced images already downloaded from EDGAR, stored alongside the page so the viewer can
/// serve them from our origin (empty when the page references none or capture is disabled).
/// </summary>
public record AsFiledHtmlCaptureResult(byte[] Html, IReadOnlyList<CapturedImage> Images)
{
    /// <summary>The filing carries no displayable exhibit (or capture is disabled) — nothing to stitch.</summary>
    public static readonly AsFiledHtmlCaptureResult None = new(null, []);

    /// <summary>A stitched as-filed page was built (with any downloaded images) and should be stored.</summary>
    public static AsFiledHtmlCaptureResult Built(
        byte[] html,
        IReadOnlyList<CapturedImage> images
    ) => new(html, images);
}

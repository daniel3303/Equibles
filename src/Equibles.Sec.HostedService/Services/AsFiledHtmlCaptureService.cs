using System.Text;
using Equibles.Integrations.Sec.Contracts;
using Equibles.Integrations.Sec.Models;
using Equibles.Sec.BusinessLogic;
using Equibles.Sec.Data.Models;
using Equibles.Sec.HostedService.Configuration;
using Equibles.Sec.HostedService.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Equibles.Sec.HostedService.Services;

/// <summary>
/// Builds the stitched "as-filed" HTML (the primary document followed by its displayable
/// exhibits, intra-filing links rewritten to in-page anchors) for a filing from its
/// already-fetched submission envelope — see <see cref="SecDocumentEnvelopeParser.TryBuildAsFiledHtml"/>.
/// Then downloads the images that page references (8-K deck slides, logos) from EDGAR so the
/// viewer can serve them from our own origin instead of hotlinking SEC (which 403s browsers).
/// Only the forms that carry the linked exhibits citations break on are stitched today (8-Ks,
/// where the Exhibit 99.1 press release lives); other forms are skipped (<see cref="AppliesTo"/>).
/// The HTML stitch costs no extra EDGAR round-trip; the image download does one fetch per image,
/// through the shared rate-limited SEC client.
/// </summary>
public class AsFiledHtmlCaptureService
{
    /// <summary>
    /// Stitcher version stamped onto a processed document. The backfill re-stitches 8-K
    /// documents whose <c>AsFiledHtmlVersion</c> is below this, so bumping it after a stitcher
    /// change reprocesses the corpus (same version-stamp redrain as the XBRL-facts extractor).
    /// Forwards to <see cref="Document.AsFiledHtmlBuilderVersion"/> — the single source of truth
    /// in the Data layer, so the backoffice can read it without depending on this assembly.
    /// </summary>
    public const int CurrentVersion = Document.AsFiledHtmlBuilderVersion;

    // Upper bound on the stitched page we'll store, matching the viewer's serve-time cap
    // (DocumentOriginalHtmlProvider.MaxOriginalHtmlBytes). A page bigger than this could never be
    // served, so we drop it at capture rather than persist an un-servable blob — the viewer then
    // degrades to the primary-only inline-iXBRL page. Keep the two caps in sync.
    private const long MaxHtmlBytes = 30L * 1024 * 1024;

    private readonly AsFiledHtmlCaptureOptions _options;
    private readonly ISecEdgarClient _secEdgarClient;
    private readonly ILogger<AsFiledHtmlCaptureService> _logger;

    public AsFiledHtmlCaptureService(
        IOptions<AsFiledHtmlCaptureOptions> options,
        ISecEdgarClient secEdgarClient,
        ILogger<AsFiledHtmlCaptureService> logger
    )
    {
        _options = options.Value;
        _secEdgarClient = secEdgarClient;
        _logger = logger;
    }

    /// <summary>The document types we stitch exhibits for. Widen this to extend coverage.</summary>
    public static bool AppliesTo(DocumentType documentType) =>
        documentType == DocumentType.EightK || documentType == DocumentType.EightKa;

    /// <summary>
    /// Builds the as-filed HTML from a full submission envelope and downloads the images it
    /// references. Returns <see cref="AsFiledHtmlCaptureResult.None"/> when capture is disabled or
    /// the filing carries no displayable exhibit. Throws on a malformed envelope — the caller
    /// decides whether that's a swallowed best-effort miss (live ingest) or a counted retry
    /// (backfill). A per-image download failure is swallowed (the image is skipped), so one bad
    /// asset never loses the stitched page.
    /// </summary>
    public async Task<AsFiledHtmlCaptureResult> Capture(
        string submissionContent,
        FilingData filing,
        CancellationToken cancellationToken = default
    )
    {
        if (!_options.Enabled)
            return AsFiledHtmlCaptureResult.None;

        if (
            !SecDocumentEnvelopeParser.TryBuildAsFiledHtml(
                submissionContent,
                filing.PrimaryDocument,
                out var html,
                out var imageFileNames
            )
        )
        {
            return AsFiledHtmlCaptureResult.None;
        }

        var bytes = Encoding.UTF8.GetBytes(html);
        // Too big to ever serve — treat as nothing to stitch so we don't store a dead blob.
        if (bytes.Length > MaxHtmlBytes)
            return AsFiledHtmlCaptureResult.None;

        var images = await DownloadImages(filing, imageFileNames, cancellationToken);
        return AsFiledHtmlCaptureResult.Built(bytes, images);
    }

    // Fetches each referenced image from the filing's EDGAR folder through the shared rate-limited
    // SEC client (compliant UA, retries, request logging). Skips a missing (404 → empty), oversized,
    // or individually-failing image rather than aborting, and stops once the per-document count or
    // total-bytes cap is hit so an image-heavy deck can't run unbounded.
    private async Task<IReadOnlyList<CapturedImage>> DownloadImages(
        FilingData filing,
        IReadOnlyList<string> fileNames,
        CancellationToken cancellationToken
    )
    {
        if (
            fileNames.Count == 0
            || string.IsNullOrEmpty(filing.Cik)
            || string.IsNullOrEmpty(filing.AccessionNumber)
        )
            return [];

        var captured = new List<CapturedImage>();
        long totalBytes = 0;

        foreach (var fileName in fileNames)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (captured.Count >= _options.MaxImagesPerDocument)
            {
                _logger.LogInformation(
                    "As-filed image count cap ({Max}) reached for {Accession}; {Skipped} image(s) skipped.",
                    _options.MaxImagesPerDocument,
                    filing.AccessionNumber,
                    fileNames.Count - captured.Count
                );
                break;
            }

            var contentType = ImageContentType(fileName);
            if (contentType == null)
                continue;

            byte[] bytes;
            try
            {
                bytes = await _secEdgarClient.GetDocumentFileBytes(
                    filing.Cik,
                    filing.AccessionNumber,
                    fileName,
                    cancellationToken
                );
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "As-filed image download failed for {File} in {Accession}; skipping it.",
                    fileName,
                    filing.AccessionNumber
                );
                continue;
            }

            if (bytes == null || bytes.Length == 0)
                continue;

            if (bytes.Length > _options.MaxImageBytes)
            {
                _logger.LogInformation(
                    "As-filed image {File} ({Size} bytes) over the per-image cap for {Accession}; skipping it.",
                    fileName,
                    bytes.Length,
                    filing.AccessionNumber
                );
                continue;
            }

            if (totalBytes + bytes.Length > _options.MaxTotalImageBytes)
            {
                _logger.LogInformation(
                    "As-filed image total-bytes cap reached for {Accession} at {Count} image(s); remaining skipped.",
                    filing.AccessionNumber,
                    captured.Count
                );
                break;
            }

            totalBytes += bytes.Length;
            captured.Add(new CapturedImage(fileName, bytes, contentType));
        }

        return captured;
    }

    // The MIME type for a bare image filename. The parser already filtered to these extensions;
    // returning null is a defensive belt-and-braces guard against an unexpected one.
    private static string ImageContentType(string fileName)
    {
        if (
            fileName.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)
        )
            return "image/jpeg";
        if (fileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            return "image/png";
        if (fileName.EndsWith(".gif", StringComparison.OrdinalIgnoreCase))
            return "image/gif";
        return null;
    }
}

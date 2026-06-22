using System.Text;
using Equibles.Integrations.Sec.Models;
using Equibles.Sec.BusinessLogic;
using Equibles.Sec.Data.Models;
using Equibles.Sec.HostedService.Configuration;
using Equibles.Sec.HostedService.Models;
using Microsoft.Extensions.Options;

namespace Equibles.Sec.HostedService.Services;

/// <summary>
/// Builds the stitched "as-filed" HTML (the primary document followed by its displayable
/// exhibits, intra-filing links rewritten to in-page anchors) for a filing from its
/// already-fetched submission envelope — see <see cref="SecDocumentEnvelopeParser.TryBuildAsFiledHtml"/>.
/// Only the forms that carry the linked exhibits citations break on are stitched today (8-Ks,
/// where the Exhibit 99.1 press release lives); other forms are skipped (<see cref="AppliesTo"/>).
/// Costs no extra EDGAR round-trip on the live ingest path.
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

    public AsFiledHtmlCaptureService(IOptions<AsFiledHtmlCaptureOptions> options)
    {
        _options = options.Value;
    }

    /// <summary>The document types we stitch exhibits for. Widen this to extend coverage.</summary>
    public static bool AppliesTo(DocumentType documentType) =>
        documentType == DocumentType.EightK || documentType == DocumentType.EightKa;

    /// <summary>
    /// Builds the as-filed HTML from a full submission envelope. Returns
    /// <see cref="AsFiledHtmlCaptureResult.None"/> when capture is disabled or the filing carries
    /// no displayable exhibit. Throws on a malformed envelope — the caller decides whether that's
    /// a swallowed best-effort miss (live ingest) or a counted retry (backfill).
    /// </summary>
    public AsFiledHtmlCaptureResult Capture(string submissionContent, FilingData filing)
    {
        if (!_options.Enabled)
            return AsFiledHtmlCaptureResult.None;

        if (
            !SecDocumentEnvelopeParser.TryBuildAsFiledHtml(
                submissionContent,
                filing.PrimaryDocument,
                out var html
            )
        )
        {
            return AsFiledHtmlCaptureResult.None;
        }

        var bytes = Encoding.UTF8.GetBytes(html);
        // Too big to ever serve — treat as nothing to stitch so we don't store a dead blob.
        return bytes.Length > MaxHtmlBytes
            ? AsFiledHtmlCaptureResult.None
            : AsFiledHtmlCaptureResult.Built(bytes);
    }
}

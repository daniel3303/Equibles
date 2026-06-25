using System.ComponentModel.DataAnnotations;
using Equibles.CommonStocks.Data.Models;
using Equibles.Sec.Data.Models.Chunks;
using Microsoft.EntityFrameworkCore;
using File = Equibles.Media.Data.Models.File;

namespace Equibles.Sec.Data.Models;

[Index(nameof(CommonStockId), nameof(DocumentType))]
[Index(nameof(DocumentType), IsUnique = false)]
[Index(nameof(ReportingDate), IsUnique = false)]
[Index(nameof(ReportingForDate), IsUnique = false)]
[Index(nameof(AccessionNumber), IsUnique = false)]
[Index(nameof(XbrlStatus), IsUnique = false)]
[Index(nameof(XbrlStatus), nameof(XbrlFactsVersion))]
[Index(nameof(DocumentType), nameof(AsFiledHtmlVersion))]
[Index(nameof(ReportedStatementsStatus), IsUnique = false)]
[Index(nameof(ReportedStatementsStatus), nameof(ReportedStatementsParseVersion))]
[Index(nameof(CreationTime), IsUnique = false)]
public class Document
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid CommonStockId { get; set; }
    public virtual CommonStock CommonStock { get; set; }

    public virtual List<Chunk> Chunks { get; set; } = [];

    /// <summary>
    /// Images referenced by this filing's as-filed HTML (8-K deck slides, logos, figures),
    /// downloaded from EDGAR and stored so the viewer can serve them from our own origin instead
    /// of hotlinking SEC. Cascade-deleted with the document; see <see cref="DocumentImage"/>.
    /// </summary>
    public virtual List<DocumentImage> Images { get; set; } = [];

    /// <summary>
    /// The file containing the document content.
    /// </summary>
    public Guid ContentId { get; set; }

    [Required]
    public virtual File Content { get; set; }

    public DocumentType DocumentType { get; set; }

    public DateOnly ReportingDate { get; set; }

    public DateOnly ReportingForDate { get; set; }

    public int LineCount { get; set; }

    [MaxLength(500)]
    public string SourceUrl { get; set; }

    /// <summary>
    /// SEC accession number of the source filing (globally unique, e.g.
    /// 0000320193-24-000123). Null for legacy rows and paper-only filings.
    /// Used to link structured financial facts back to their filing.
    /// </summary>
    [MaxLength(32)]
    public string AccessionNumber { get; set; }

    /// <summary>
    /// Comma-joined SEC item numbers reported by this filing, e.g. <c>"2.02,9.01"</c> on an
    /// 8-K. Lets a consumer pick out earnings-release 8-Ks (Item 2.02 "Results of Operations
    /// and Financial Condition") without re-parsing. Null for forms that carry no items and
    /// for legacy rows ingested before capture; the empty string is the backfill's terminal
    /// "checked, nothing found in the feed" marker (consumers treat both as no items, but
    /// only null rows are still pending backfill).
    /// </summary>
    [MaxLength(256)]
    public string Items { get; set; }

    /// <summary>
    /// Tracks whether the filing's raw XBRL envelope has been captured. Defaults to
    /// <see cref="XbrlCaptureStatus.NotChecked"/> so existing rows and freshly-ingested
    /// documents are picked up by the backfill until examined.
    /// </summary>
    public XbrlCaptureStatus XbrlStatus { get; set; } = XbrlCaptureStatus.NotChecked;

    /// <summary>
    /// Which kind of XBRL envelope was captured. Null until <see cref="XbrlStatus"/> is
    /// <see cref="XbrlCaptureStatus.Captured"/>.
    /// </summary>
    public XbrlType? XbrlType { get; set; }

    /// <summary>
    /// The file holding the gzip-compressed raw XBRL envelope. Null when no envelope was
    /// captured (status <see cref="XbrlCaptureStatus.NotChecked"/> or
    /// <see cref="XbrlCaptureStatus.NotPresent"/>). The stored (compressed) size is
    /// <c>XbrlContent.Size</c>; the pre-compression size is <see cref="XbrlUncompressedSize"/>.
    /// </summary>
    public Guid? XbrlContentId { get; set; }
    public virtual File XbrlContent { get; set; }

    /// <summary>Size in bytes of the XBRL envelope before gzip compression. Null when none captured.</summary>
    public long? XbrlUncompressedSize { get; set; }

    /// <summary>
    /// How many times the backfill has tried (and failed to reach a terminal result) to
    /// capture this document's XBRL. The backfill stops selecting a document once this hits
    /// its retry ceiling, so a permanently-unfetchable filing can't starve the rest of the
    /// queue. Only meaningful while <see cref="XbrlStatus"/> is
    /// <see cref="XbrlCaptureStatus.NotChecked"/>.
    /// </summary>
    public int XbrlCaptureAttempts { get; set; }

    /// <summary>
    /// Retry ceiling for <see cref="XbrlCaptureAttempts"/>: once a
    /// <see cref="XbrlCaptureStatus.NotChecked"/> document has failed to reach a terminal
    /// capture this many times it leaves the backfill working set, so a permanently
    /// unfetchable filing can't starve the rest of the queue.
    /// </summary>
    public const int MaxXbrlCaptureAttempts = 5;

    /// <summary>
    /// Version of the dimensional-fact extractor that last processed this document's
    /// captured XBRL envelope. 0 = never extracted; the extraction sweep selects
    /// <see cref="XbrlCaptureStatus.Captured"/> documents whose version is below the
    /// extractor's current one, so bumping the extractor version reprocesses the corpus
    /// (same pattern as the N-PORT / insider ParserVersion reprocess).
    /// </summary>
    public int XbrlFactsVersion { get; set; }

    /// <summary>
    /// How many times the dimensional-fact extraction has failed on this document. The
    /// sweep stops selecting a document at its retry ceiling so one unparseable envelope
    /// can't starve the queue. Only meaningful while <see cref="XbrlFactsVersion"/> is
    /// below the extractor's current version.
    /// </summary>
    public int XbrlFactsAttempts { get; set; }

    // --- As-filed display HTML (the primary document + its displayable exhibits, stitched) ---
    // Kept SEPARATE from XbrlContent (which the dimensional-fact extractor parses): the as-filed
    // viewer needs the WHOLE filing — an 8-K's cover page PLUS its linked exhibits, e.g. the
    // Exhibit 99.1 press release — so a citation grounded in an exhibit can be pinpointed and the
    // cover page's exhibit links resolve in-page. Building it as its own artifact keeps display
    // enrichment from ever perturbing fact extraction. Only populated for filings that carry a
    // displayable exhibit (currently 8-Ks); null otherwise.

    /// <summary>
    /// The file holding the gzip-compressed stitched as-filed HTML, or null when none was built
    /// (no displayable exhibit, or not yet processed). The pre-compression size is
    /// <see cref="AsFiledHtmlUncompressedSize"/>.
    /// </summary>
    public Guid? AsFiledHtmlContentId { get; set; }
    public virtual File AsFiledHtmlContent { get; set; }

    /// <summary>Size in bytes of the stitched as-filed HTML before gzip compression. Null when none built.</summary>
    public long? AsFiledHtmlUncompressedSize { get; set; }

    /// <summary>
    /// Version of the as-filed HTML stitcher that last processed this document. 0 = never built.
    /// The backfill selects 8-K documents whose version is below the builder's current one, so
    /// bumping the builder version re-stitches the corpus (same version-stamp redrain as the
    /// XBRL-facts extractor). A filing examined and found to carry no displayable exhibit is
    /// stamped current with a null <see cref="AsFiledHtmlContentId"/> so it isn't re-fetched.
    /// </summary>
    public int AsFiledHtmlVersion { get; set; }

    /// <summary>
    /// How many times building the as-filed HTML (fetch/stitch) has failed for this document.
    /// The backfill stops selecting it at the ceiling so one unbuildable filing can't starve
    /// the queue.
    /// </summary>
    public int AsFiledHtmlAttempts { get; set; }

    /// <summary>Retry ceiling for <see cref="AsFiledHtmlAttempts"/>.</summary>
    public const int MaxAsFiledHtmlAttempts = 5;

    /// <summary>
    /// Current as-filed HTML stitcher version — the single source of truth for "which documents
    /// still need stitching". Lives here (Data) rather than in the worker so both the backfill
    /// work-set query and the backoffice "pending" metric can reference it without depending on
    /// the hosted-service assembly. Bump it after a stitcher change to re-stitch the corpus.
    /// </summary>
    /// <remarks>
    /// Bumped to 2 when the stitcher started downloading the filing's referenced images (8-K deck
    /// slides, logos) into <see cref="Images"/>; the backfill re-processes the corpus to pull
    /// images for filings stitched by the image-less v1 builder.
    /// </remarks>
    public const int AsFiledHtmlBuilderVersion = 2;

    // --- As-reported statement capture (SEC's pre-rendered R-files) ---
    // SEC renders each financial statement of an XBRL filing into an HTML table (R2.htm,
    // R3.htm…) from the filing's own presentation/calculation/label linkbases, and indexes
    // them in FilingSummary.xml. We capture FilingSummary + the "Statements" R-files here as
    // one gzip bundle (the network step), then a separate local parse step turns them into
    // ReportedFinancialStatement rows. Kept apart from XbrlContent (fact extraction) and
    // AsFiledHtmlContent (the human viewer) so presentation capture perturbs neither.

    /// <summary>
    /// Whether the filing's as-reported statement bundle has been captured. Default
    /// <see cref="XbrlCaptureStatus.NotChecked"/> is the backfill target;
    /// <see cref="XbrlCaptureStatus.Captured"/> and <see cref="XbrlCaptureStatus.NotPresent"/>
    /// (no FilingSummary / no XBRL) are terminal.
    /// </summary>
    public XbrlCaptureStatus ReportedStatementsStatus { get; set; } = XbrlCaptureStatus.NotChecked;

    /// <summary>
    /// The file holding the gzip bundle of FilingSummary.xml plus the "Statements" R-files,
    /// or null when none was captured. The pre-compression size is
    /// <see cref="ReportedStatementsUncompressedSize"/>.
    /// </summary>
    public Guid? ReportedStatementsContentId { get; set; }
    public virtual File ReportedStatementsContent { get; set; }

    /// <summary>Size in bytes of the R-file bundle before gzip compression. Null when none captured.</summary>
    public long? ReportedStatementsUncompressedSize { get; set; }

    /// <summary>
    /// How many times capturing the R-file bundle has failed for this document. The backfill
    /// stops selecting it at <see cref="MaxReportedStatementsCaptureAttempts"/> so a
    /// permanently-unfetchable filing can't starve the queue. Only meaningful while
    /// <see cref="ReportedStatementsStatus"/> is <see cref="XbrlCaptureStatus.NotChecked"/>.
    /// </summary>
    public int ReportedStatementsCaptureAttempts { get; set; }

    /// <summary>Retry ceiling for <see cref="ReportedStatementsCaptureAttempts"/>.</summary>
    public const int MaxReportedStatementsCaptureAttempts = 5;

    /// <summary>
    /// Version of the R-file parser that last reconstructed this document's statements from the
    /// captured bundle. 0 = never parsed; the parse sweep selects
    /// <see cref="XbrlCaptureStatus.Captured"/> documents whose version is below
    /// <see cref="ReportedStatementsParserVersion"/>, so bumping the parser re-derives the
    /// corpus offline without re-fetching EDGAR (same redrain as the XBRL-facts extractor).
    /// </summary>
    public int ReportedStatementsParseVersion { get; set; }

    /// <summary>
    /// How many times parsing the captured R-file bundle has failed for this document. The
    /// parse sweep stops selecting it at <see cref="MaxReportedStatementsParseAttempts"/> so
    /// one unparseable bundle can't starve the queue.
    /// </summary>
    public int ReportedStatementsParseAttempts { get; set; }

    /// <summary>Retry ceiling for <see cref="ReportedStatementsParseAttempts"/>.</summary>
    public const int MaxReportedStatementsParseAttempts = 5;

    /// <summary>
    /// Current R-file parser version — the single source of truth for "which captured filings
    /// still need (re)parsing". Lives here (Data) so both the parse work-set query and the
    /// backoffice "pending" metric can reference it without depending on the hosted-service
    /// assembly. Bump after a parser change to re-derive the corpus.
    /// </summary>
    public const int ReportedStatementsParserVersion = 1;

    public DateTime CreationTime { get; set; } = DateTime.UtcNow;
}

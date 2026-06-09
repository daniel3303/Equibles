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
public class Document
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid CommonStockId { get; set; }
    public virtual CommonStock CommonStock { get; set; }

    public virtual List<Chunk> Chunks { get; set; } = [];

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
    /// for legacy rows ingested before capture.
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

    public DateTime CreationTime { get; set; } = DateTime.UtcNow;
}

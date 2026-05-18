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

    public DateTime CreationTime { get; set; } = DateTime.UtcNow;
}

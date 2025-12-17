using System.ComponentModel.DataAnnotations;
using Equibles.ParadeDB.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Sec.Data.Models.Chunks;

[Index(nameof(DocumentId), nameof(Index), IsUnique = true)]
[Index(nameof(DocumentType), IsUnique = false)]
[Bm25Index(nameof(Id), nameof(Content), nameof(DocumentType), nameof(DocumentId), nameof(Ticker), nameof(ReportingDate))]
public class Chunk {
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// The index of the chunk within the parent document.
    /// </summary>
    public int Index { get; set; }

    /// <summary>
    /// The start position of the chunk within the parent document.
    /// </summary>
    public int StartPosition { get; set; }

    /// <summary>
    /// The end position of the chunk within the parent document.
    /// </summary>
    public int EndPosition { get; set; }

    /// <summary>
    /// The approximate 1-based line number where this chunk starts in the original document.
    /// </summary>
    public int StartLineNumber { get; set; }

    public string Content { get; set; }

    /// <summary>
    /// Denormalized from Document for performance — allows hybrid search to filter
    /// by document type without joining the Document table.
    /// </summary>
    public DocumentType DocumentType { get; set; }

    /// <summary>
    /// Denormalized from <see cref="Document"/>.<see cref="Documents.Document.CommonStock"/>.<see cref="CommonStocks.CommonStock.Ticker"/>.
    /// Stored on the chunk so Tantivy can filter by ticker without SQL joins.
    /// </summary>
    [MaxLength(20)]
    public string Ticker { get; set; }

    /// <summary>
    /// Denormalized from <see cref="Document"/>.<see cref="Documents.Document.ReportingDate"/>.
    /// Stored as DateTime (converted from DateOnly) so Tantivy can use native datetime range queries.
    /// </summary>
    public DateTime ReportingDate { get; set; }

    public virtual Document Document { get; set; }
    public Guid DocumentId { get; set; }

    public virtual List<Embedding> Embeddings { get; set; } = [];

    public DateTime CreationTime { get; set; } = DateTime.UtcNow;
}

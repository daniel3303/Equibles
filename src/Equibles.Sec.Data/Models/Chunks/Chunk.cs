using System.ComponentModel.DataAnnotations;
using Equibles.ParadeDB.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Sec.Data.Models.Chunks;

[Index(nameof(DocumentId), nameof(Index), IsUnique = true)]
[Index(nameof(DocumentType), IsUnique = false)]
[Index(nameof(CreationTime))]
// Btree on Ticker: the hybrid searcher's ticker-scoped vector arm reaches a company's
// embeddings through this index (Chunk by ticker → Embedding by ChunkId) — without it the
// planner distance-sorts the whole Embedding table before the join filters it. The BM25 index
// below also carries Ticker, but only for Tantivy term filters, not SQL equality.
[Index(nameof(Ticker))]
[Bm25Index(
    nameof(Id),
    nameof(Content),
    nameof(DocumentType),
    nameof(DocumentId),
    nameof(Ticker),
    nameof(ReportingDate)
)]
public class Chunk
{
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
    /// <remarks>
    /// Indexed with the <c>raw</c> tokenizer so the whole symbol is a single token.
    /// The default tokenizer splits on dash/slash, which would shatter class-share
    /// tickers (e.g. <c>BRK-B</c> → <c>brk</c>/<c>b</c>) and make an exact term filter
    /// impossible. <c>Fast = true</c> stores it columnar so the filter can be pushed
    /// into the BM25 index instead of post-filtering scored chunks on the heap (#2157).
    /// </remarks>
    [MaxLength(20)]
    [Bm25Text(Tokenizer = Bm25Tokenizer.Raw, Fast = true)]
    public string Ticker { get; set; }

    /// <summary>
    /// Denormalized from <see cref="Document"/>.<see cref="Documents.Document.ReportingDate"/>.
    /// Stored as DateTime (converted from DateOnly) for the search index. This is a cache, not the
    /// filing-date source of truth: retrieval filters and renderers use the parent document date so
    /// a corrected document cannot leave stale transcript search metadata (#7049).
    /// </summary>
    public DateTime ReportingDate { get; set; }

    public virtual Document Document { get; set; }
    public Guid DocumentId { get; set; }

    public virtual List<Embedding> Embeddings { get; set; } = [];

    public DateTime CreationTime { get; set; } = DateTime.UtcNow;
}

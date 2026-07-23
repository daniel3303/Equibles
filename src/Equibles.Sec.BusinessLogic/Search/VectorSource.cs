namespace Equibles.Sec.BusinessLogic.Search;

/// <summary>
/// Where the semantic (vector) arm of <see cref="HybridChunkSearcher"/> gets its ranking.
/// </summary>
public enum VectorSource
{
    /// <summary>No vector arm — BM25 only. The hybrid searcher behaves exactly like the old
    /// keyword-only path.</summary>
    Off,

    /// <summary>
    /// Re-rank the BM25 candidate pool by cosine similarity using the chunks' STORED vectors,
    /// embedding only the query live. Improves the ordering of what BM25 already found (cannot
    /// surface a chunk BM25 never retrieved). Fast — one query embed plus an indexed by-id vector
    /// lookup, no ANN index — and scales with backfill coverage: pooled chunks without a vector yet
    /// just keep their BM25 rank. This is the production form of the bake-off's validated re-rank.
    /// Exception: a DOCUMENT-scoped search always uses the exhaustive in-document vector ranking
    /// instead — one document's chunks are a bounded set served by btree indexes, no ANN index
    /// needed — so a purely semantic query can find passages BM25 missed within that document.
    /// </summary>
    Pool,

    /// <summary>
    /// Query the populated pgvector Embedding table for corpus-wide nearest neighbours, then fuse
    /// with BM25. Can surface chunks BM25 missed, but requires the embedding backfill to be
    /// complete and an ANN (HNSW) index on the vector column to stay inside the query budget.
    /// </summary>
    Table,
}

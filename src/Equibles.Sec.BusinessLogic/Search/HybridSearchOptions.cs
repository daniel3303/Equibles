namespace Equibles.Sec.BusinessLogic.Search;

/// <summary>
/// Tunables for <see cref="HybridChunkSearcher"/>, bound from the "HybridSearch" configuration
/// section. Defaults give the validated BM25 + on-the-fly vector re-rank (RRF) blend; the vector
/// arm self-disables when the embedding server is unavailable, so these are safe out of the box.
/// </summary>
public class HybridSearchOptions
{
    /// <summary>Master switch for the semantic arm. When false the searcher is pure BM25.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Where the semantic ranking comes from. See <see cref="Search.VectorSource"/>.</summary>
    public VectorSource VectorSource { get; set; } = VectorSource.Pool;

    /// <summary>
    /// How many BM25 hits feed the vector arm before fusion. The re-rank reads the pool's STORED
    /// vectors (one indexed lookup) and embeds only the query, so a larger pool costs almost
    /// nothing — it just gives the re-rank more of the BM25 top to reorder.
    /// </summary>
    public int CandidatePoolSize { get; set; } = 100;

    /// <summary>The RRF constant. Higher flattens the contribution of top ranks. 60 is the
    /// ParadeDB-documented default.</summary>
    public int RrfK { get; set; } = RrfFusion.DefaultK;

    /// <summary>
    /// Hard wall-clock budget for the whole semantic arm (query embed + pool embeds or corpus
    /// lookup). When it elapses the searcher abandons the vector arm and returns the BM25 ranking,
    /// so a slow or unreachable embedding server can never stall a search beyond this. The global
    /// search aggregator abandons a provider after ~5s but does NOT cancel it, so this is what
    /// actually bounds the orphaned work.
    /// </summary>
    public int SemanticTimeoutSeconds { get; set; } = 4;
}

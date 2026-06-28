using Equibles.Core.AutoWiring;
using Equibles.Sec.BusinessLogic.Embeddings;
using Equibles.Sec.Data.Models;
using Equibles.Sec.Data.Models.Chunks;
using Equibles.Sec.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Equibles.Sec.BusinessLogic.Search;

/// <summary>
/// Single entry point for chunk retrieval. Runs the ParadeDB BM25 ranking and, when the embedding
/// server is available and a vector source is configured, a semantic ranking, then fuses the two
/// with Reciprocal Rank Fusion. The semantic arm is strictly additive: if embeddings are disabled,
/// the query can't be embedded, or the vector lookup fails, the result is the plain BM25 ranking —
/// the searcher never throws on the vector path and never returns fewer hits than BM25 alone.
/// </summary>
[Service(ServiceLifetime.Scoped)]
public class HybridChunkSearcher
{
    private readonly ChunkRepository _chunkRepository;
    private readonly EmbeddingRepository _embeddingRepository;
    private readonly IEmbeddingClient _embeddingClient;
    private readonly HybridSearchOptions _options;
    private readonly string _model;
    private readonly ILogger<HybridChunkSearcher> _logger;

    public HybridChunkSearcher(
        ChunkRepository chunkRepository,
        EmbeddingRepository embeddingRepository,
        IEmbeddingClient embeddingClient,
        IOptions<HybridSearchOptions> options,
        IOptions<EmbeddingConfig> embeddingConfig,
        ILogger<HybridChunkSearcher> logger
    )
    {
        _chunkRepository = chunkRepository;
        _embeddingRepository = embeddingRepository;
        _embeddingClient = embeddingClient;
        _options = options.Value;
        _model = embeddingConfig.Value.ModelName;
        _logger = logger;
    }

    public async Task<List<Chunk>> Search(
        string query,
        int maxResults,
        string ticker = null,
        Guid? documentId = null,
        DocumentType documentType = null,
        DateOnly? startDate = null,
        DateOnly? endDate = null,
        CancellationToken cancellationToken = default
    )
    {
        // When the semantic arm is live, BM25 returns a deeper pool so RRF has more to reorder;
        // otherwise it returns exactly what the caller asked for.
        var semanticActive =
            _options.Enabled
            && _options.VectorSource != VectorSource.Off
            && _embeddingClient.IsEnabled;
        var bm25Limit = semanticActive
            ? Math.Max(maxResults, _options.CandidatePoolSize)
            : maxResults;

        var bm25 = await _chunkRepository.HybridSearch(
            query,
            bm25Limit,
            ticker,
            documentId,
            documentType,
            startDate,
            endDate,
            cancellationToken
        );

        if (!semanticActive || bm25.Count == 0)
            return bm25.Take(maxResults).ToList();

        // Bound the whole semantic arm: the global search aggregator abandons a slow provider but
        // doesn't cancel it, and the embedding server is shared with the backfill — so cap the
        // wall-clock here, linked to the caller's token, and degrade to BM25 if it elapses.
        using var semanticCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        semanticCts.CancelAfter(TimeSpan.FromSeconds(_options.SemanticTimeoutSeconds));

        var vectorIds = await RankSemantically(
            query,
            bm25,
            ticker,
            documentId,
            documentType,
            startDate,
            endDate,
            semanticCts.Token
        );
        if (vectorIds.Count == 0)
            return bm25.Take(maxResults).ToList();

        var bm25Ids = bm25.Select(chunk => chunk.Id).ToList();
        var fusedIds = RrfFusion
            .Fuse([bm25Ids, vectorIds], _options.RrfK)
            .Take(maxResults)
            .ToList();

        return await MaterializeInOrder(fusedIds, bm25, cancellationToken);
    }

    // Produces a semantic ranking of chunk ids, swallowing any embedding-server failure into an
    // empty list so retrieval degrades to BM25 rather than erroring.
    private async Task<List<Guid>> RankSemantically(
        string query,
        IReadOnlyList<Chunk> bm25Pool,
        string ticker,
        Guid? documentId,
        DocumentType documentType,
        DateOnly? startDate,
        DateOnly? endDate,
        CancellationToken cancellationToken
    )
    {
        try
        {
            return _options.VectorSource switch
            {
                VectorSource.Pool => await RankPool(query, bm25Pool, cancellationToken),
                VectorSource.Table => await RankCorpus(
                    query,
                    ticker,
                    documentId,
                    documentType,
                    startDate,
                    endDate,
                    cancellationToken
                ),
                _ => [],
            };
        }
        catch (Exception exception)
        {
            // Covers an embedding-server failure AND the SemanticTimeoutSeconds budget elapsing
            // (OperationCanceledException) — both degrade to the BM25 ranking we already have.
            _logger.LogWarning(
                exception,
                "Semantic ranking failed or timed out; falling back to BM25-only results"
            );
            return [];
        }
    }

    // Re-ranks the BM25 pool by cosine similarity to the query using the chunks' STORED vectors —
    // the ones the backfill writes — so the only live embedding call is the query itself. (An
    // earlier version re-embedded every candidate on the fly; that needed ~N requests per query
    // and was unusable against a single-slot embedding server.) Candidates without a stored vector
    // yet simply don't get a semantic score and keep their BM25 rank through the fusion, so this
    // scales gracefully with backfill coverage and needs no ANN index — vectors are fetched by
    // chunk id through the unique (ChunkId, Model) index.
    private async Task<List<Guid>> RankPool(
        string query,
        IReadOnlyList<Chunk> candidates,
        CancellationToken cancellationToken
    )
    {
        var pool =
            candidates.Count > _options.CandidatePoolSize
                ? candidates.Take(_options.CandidatePoolSize).ToList()
                : candidates;
        if (pool.Count == 0)
            return [];

        var queryVector = await _embeddingClient.GenerateEmbedding(query, cancellationToken);
        if (queryVector == null)
            return [];

        var storedVectors = await _embeddingRepository
            .GetByChunks(pool)
            .Where(embedding => embedding.Model == _model)
            .Select(embedding => new { embedding.ChunkId, embedding.Vector })
            .ToListAsync(cancellationToken);

        var scored = new List<(Guid Id, double Similarity)>();
        foreach (var stored in storedVectors)
            scored.Add((stored.ChunkId, CosineSimilarity(queryVector, stored.Vector.ToArray())));

        return scored
            .OrderByDescending(entry => entry.Similarity)
            .Select(entry => entry.Id)
            .ToList();
    }

    // Corpus-wide nearest neighbours from the populated pgvector table, scoped to the same filters
    // BM25 applies. Surfaces relevant chunks BM25 never retrieved.
    private async Task<List<Guid>> RankCorpus(
        string query,
        string ticker,
        Guid? documentId,
        DocumentType documentType,
        DateOnly? startDate,
        DateOnly? endDate,
        CancellationToken cancellationToken
    )
    {
        var queryVector = await _embeddingClient.GenerateEmbedding(query, cancellationToken);
        if (queryVector == null)
            return [];

        return await _embeddingRepository.SearchSimilarChunks(
            queryVector,
            _model,
            _options.CandidatePoolSize,
            ticker,
            documentId,
            documentType,
            ToUtc(startDate),
            ToUtc(endDate),
            cancellationToken
        );
    }

    // Resolves fused ids back to Chunk entities in fused order. BM25 chunks are already loaded; the
    // corpus arm can contribute ids outside the BM25 pool, so those are fetched in one query.
    private async Task<List<Chunk>> MaterializeInOrder(
        List<Guid> orderedIds,
        IReadOnlyList<Chunk> bm25Pool,
        CancellationToken cancellationToken
    )
    {
        var byId = bm25Pool.ToDictionary(chunk => chunk.Id);

        var missing = orderedIds.Where(id => !byId.ContainsKey(id)).ToList();
        if (missing.Count > 0)
        {
            var fetched = await _chunkRepository
                .GetAll()
                .Where(chunk => missing.Contains(chunk.Id))
                .ToListAsync(cancellationToken);
            foreach (var chunk in fetched)
                byId[chunk.Id] = chunk;
        }

        return orderedIds.Where(id => byId.ContainsKey(id)).Select(id => byId[id]).ToList();
    }

    private static DateTime? ToUtc(DateOnly? date)
    {
        if (!date.HasValue)
            return null;

        return DateTime.SpecifyKind(date.Value.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
    }

    private static double CosineSimilarity(float[] a, float[] b)
    {
        double dot = 0,
            normA = 0,
            normB = 0;
        var length = Math.Min(a.Length, b.Length);
        for (var i = 0; i < length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        if (normA == 0 || normB == 0)
            return 0;

        return dot / (Math.Sqrt(normA) * Math.Sqrt(normB));
    }
}

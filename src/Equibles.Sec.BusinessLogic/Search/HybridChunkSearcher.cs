using System.Runtime.ExceptionServices;
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
/// A BM25 pass that blows its statement budget (<see cref="ChunkSearchTimeoutException"/>) degrades
/// the same way — the other pass and the vector arm still run; the timeout only resurfaces when the
/// search would otherwise return a false "no matches". A non-empty degraded result may therefore be
/// semantic-only (the keyword passes timed out while the vector arm answered) — that trade is
/// accepted deliberately: a somewhat weaker ranking beats an error, and the warning log is the
/// trace. The pass after a timed-out pass runs on a tighter statement budget, so one search can
/// never hold a connection for much more than a single full budget plus that reduced one.
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

    // How much deeper the BM25 pool goes when a per-company cap is active: the cap
    // discards a dominant filer's surplus hits, so the pool must hold enough distinct
    // companies to refill the requested result count.
    private const int PerCompanyOverFetchFactor = 5;

    // Statement budget for a BM25 pass that runs AFTER another pass already timed out.
    // The timed-out pass warmed the index pages it died on (measured on the production
    // corpus: 6.2s cold vs 1.25s for the warm disjunctive pass), so a tighter budget
    // still lets the degrade succeed while bounding what one search can pin a database
    // connection for — the pair can never burn more than one full budget plus this.
    private const int DegradedPassTimeoutSeconds = 3;
    private const int TickerScopedPassTimeoutSeconds = 3;

    public async Task<List<Chunk>> Search(
        string query,
        int maxResults,
        string ticker = null,
        IReadOnlyCollection<string> excludeTickers = null,
        Guid? documentId = null,
        IReadOnlyCollection<DocumentType> documentTypes = null,
        int maxResultsPerCompany = 0,
        DateOnly? startDate = null,
        DateOnly? endDate = null,
        bool disjunctiveFallback = false,
        CancellationToken cancellationToken = default
    )
    {
        // When the semantic arm is live, BM25 returns a deeper pool so RRF has more to reorder;
        // otherwise it returns exactly what the caller asked for. A per-company cap also deepens
        // the pool: capping discards surplus hits, and the pool must still fill maxResults.
        var semanticActive =
            _options.Enabled
            && _options.VectorSource != VectorSource.Off
            && _embeddingClient.IsEnabled;

        // The corpus vector arm ranks stored vectors directly, so it can surface chunks BM25
        // never retrieved — but corpus-WIDE it needs an ANN index to stay inside the query
        // budget, so it only runs when configured (VectorSource.Table). A DOCUMENT-scoped
        // search is the exception: one document's chunks are a bounded set served by the
        // existing btree indexes, so the exhaustive in-document ranking is always safe — and
        // it is what makes a purely semantic question (zero token overlap with the filing's
        // wording) findable at all. Under Auto (the default) a TICKER-scoped search takes the
        // same exhaustive path: a company's chunks are a bounded set reached through the Chunk
        // ticker btree index (measured ~250ms for a large filer vs 85s corpus-wide), and the
        // SemanticTimeoutSeconds budget still bounds an outlier company.
        var corpusArmSafe =
            semanticActive
            && (
                _options.VectorSource == VectorSource.Table
                || documentId.HasValue
                || (
                    _options.VectorSource == VectorSource.Auto && !string.IsNullOrWhiteSpace(ticker)
                )
            );
        var bm25Limit = maxResults;
        if (semanticActive)
            bm25Limit = Math.Max(bm25Limit, _options.CandidatePoolSize);
        if (maxResultsPerCompany > 0)
            bm25Limit = Math.Max(bm25Limit, maxResults * PerCompanyOverFetchFactor);

        List<Chunk> bm25;
        ChunkSearchTimeoutException bm25Timeout = null;
        var companyFallbackAnswered = false;
        try
        {
            bm25 = await _chunkRepository.HybridSearch(
                query,
                bm25Limit,
                ticker,
                excludeTickers,
                documentId,
                documentTypes,
                startDate,
                endDate,
                commandTimeoutSeconds: ticker != null ? TickerScopedPassTimeoutSeconds : null,
                cancellationToken: cancellationToken
            );
        }
        catch (ChunkSearchTimeoutException exception)
        {
            // A cold BM25 index (first touch after a Postgres restart) can push a long
            // conjunctive query past its statement budget. Degrade to an empty pool
            // instead of failing the whole search: the disjunctive fallback and the
            // corpus vector arm below can still answer — and the timed-out statement
            // itself warmed the index pages, so they usually do. The timeout is kept so
            // an empty final result surfaces it rather than reading as "no matches".
            _logger.LogWarning(exception, "Conjunctive BM25 pass timed out; degrading");
            bm25 = [];
            bm25Timeout = exception;

            if (ticker != null)
            {
                try
                {
                    bm25 = await _chunkRepository.HybridSearchCompanyFallback(
                        query,
                        bm25Limit,
                        ticker,
                        documentId,
                        documentTypes,
                        startDate,
                        endDate,
                        cancellationToken
                    );
                    if (bm25.Count > 0)
                    {
                        companyFallbackAnswered = true;
                        bm25Timeout = null;
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception fallbackException)
                    when (!cancellationToken.IsCancellationRequested)
                {
                    _logger.LogWarning(
                        fallbackException,
                        "Company-local full-text fallback failed; continuing search degradation"
                    );
                }
            }
        }

        // Opt-in recall fallback: BM25 ANDs every query token, so a wordy natural-language
        // query where a single token has no match ("drivers" vs the filing's "driven")
        // excludes every on-point chunk. When the conjunctive pass can't fill the request,
        // top up from a disjunctive (any-token) pass — conjunctive hits keep their rank and
        // the broader hits only append after them, so precise matches never lose position.
        if (!companyFallbackAnswered && disjunctiveFallback && bm25.Count < maxResults)
        {
            try
            {
                var disjunctive = await _chunkRepository.HybridSearch(
                    query,
                    bm25Limit,
                    ticker,
                    excludeTickers,
                    documentId,
                    documentTypes,
                    startDate,
                    endDate,
                    conjunctive: false,
                    // After a timed-out conjunctive pass the fallback runs on the pages
                    // that pass just warmed — tighten its budget so the pair stays
                    // bounded (see DegradedPassTimeoutSeconds).
                    commandTimeoutSeconds: bm25Timeout != null ? DegradedPassTimeoutSeconds : null,
                    cancellationToken: cancellationToken
                );
                var seen = bm25.Select(chunk => chunk.Id).ToHashSet();
                bm25 = bm25.Concat(disjunctive.Where(chunk => !seen.Contains(chunk.Id))).ToList();
                // The disjunctive pass matches a superset of the conjunctive pass, so its
                // completed result also answers for a timed-out conjunctive pass — an
                // empty pool now genuinely means "no matches", not "ran out of budget".
                bm25Timeout = null;
            }
            catch (ChunkSearchTimeoutException exception)
            {
                _logger.LogWarning(
                    exception,
                    "Disjunctive BM25 fallback timed out; keeping the conjunctive results"
                );
                bm25Timeout ??= exception;
            }
        }

        // An empty result after a timed-out BM25 pass is NOT a proven "no matches" —
        // returning it would read as "the filings say nothing about this". Surface the
        // timeout instead; a retry hits the index pages the failed passes just warmed.
        // Declared below the fallback block on purpose: it reads bm25Timeout at call
        // time, and every mutation of that variable happens above this line.
        List<Chunk> ThrowIfEmptyAfterTimeout(List<Chunk> results)
        {
            if (results.Count == 0 && bm25Timeout != null)
                ExceptionDispatchInfo.Capture(bm25Timeout).Throw();
            return results;
        }

        // The company-local fallback already returned proven full-text matches from a bounded
        // ticker slice. Return them immediately instead of spending another statement budget on
        // the index or semantic arms that just failed under the same load.
        if (companyFallbackAnswered)
        {
            return ApplyPoolControls(bm25, excludeTickers, documentTypes, maxResultsPerCompany)
                .Take(maxResults)
                .ToList();
        }

        // With only the pool re-rank available, an empty BM25 pool leaves the semantic arm
        // nothing to work on; with the corpus arm safe (Table mode, any document scope, or
        // Auto + ticker scope) the vector ranking can carry the result alone.
        if (!semanticActive || (bm25.Count == 0 && !corpusArmSafe))
            return ThrowIfEmptyAfterTimeout(
                ApplyPoolControls(bm25, excludeTickers, documentTypes, maxResultsPerCompany)
                    .Take(maxResults)
                    .ToList()
            );

        // Bound the whole semantic arm: the global search aggregator abandons a slow provider but
        // doesn't cancel it, and the embedding server is shared with the backfill — so cap the
        // wall-clock here, linked to the caller's token, and degrade to BM25 if it elapses.
        using var semanticCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        semanticCts.CancelAfter(TimeSpan.FromSeconds(_options.SemanticTimeoutSeconds));

        // The corpus-wide vector arm scopes by a single document type; with several
        // requested it runs unscoped and the type filter is enforced post-fusion below.
        var vectorIds = await RankSemantically(
            query,
            bm25,
            corpusArmSafe,
            ticker,
            documentId,
            documentTypes is { Count: 1 } ? documentTypes.First() : null,
            startDate,
            endDate,
            semanticCts.Token
        );
        if (vectorIds.Count == 0)
            return ThrowIfEmptyAfterTimeout(
                ApplyPoolControls(bm25, excludeTickers, documentTypes, maxResultsPerCompany)
                    .Take(maxResults)
                    .ToList()
            );

        var bm25Ids = bm25.Select(chunk => chunk.Id).ToList();
        // Fuse the full pool (not just maxResults): the pool controls below discard
        // hits, so the fused list must stay deep enough to refill the result count.
        var fusedIds = RrfFusion.Fuse([bm25Ids, vectorIds], _options.RrfK).ToList();

        var fused = await MaterializeInOrder(fusedIds, bm25, cancellationToken);
        return ThrowIfEmptyAfterTimeout(
            ApplyPoolControls(fused, excludeTickers, documentTypes, maxResultsPerCompany)
                .Take(maxResults)
                .ToList()
        );
    }

    // The pool controls, re-applied AFTER fusion: the BM25 arm already resolves
    // exclusions and type filters inside the index, but the corpus vector arm knows
    // neither, so a fused list can reintroduce an excluded ticker or an unrequested
    // type. The per-company cap keeps each filer's best-ranked chunks in relevance
    // order — one chatty filer must not fill the whole result set. Chunks without a
    // ticker pass the cap untouched (they cannot flood by company).
    //
    // SINGLE-ENUMERATION ONLY: the cap closes over a mutable per-ticker counter, so a
    // second enumeration of the same returned value would see spent counters and drop
    // everything. Every caller consumes it exactly once via .Take(...).ToList().
    private static IEnumerable<Chunk> ApplyPoolControls(
        IEnumerable<Chunk> chunks,
        IReadOnlyCollection<string> excludeTickers,
        IReadOnlyCollection<DocumentType> documentTypes,
        int maxResultsPerCompany
    )
    {
        if (excludeTickers is { Count: > 0 })
        {
            var excluded = new HashSet<string>(excludeTickers, StringComparer.OrdinalIgnoreCase);
            chunks = chunks.Where(chunk =>
                chunk.Ticker == null || !excluded.Contains(chunk.Ticker)
            );
        }

        if (documentTypes is { Count: > 1 })
            chunks = chunks.Where(chunk => documentTypes.Contains(chunk.DocumentType));

        if (maxResultsPerCompany <= 0)
            return chunks;

        var perTicker = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        return chunks.Where(chunk =>
        {
            if (chunk.Ticker == null)
                return true;
            var count = perTicker.GetValueOrDefault(chunk.Ticker);
            if (count >= maxResultsPerCompany)
                return false;
            perTicker[chunk.Ticker] = count + 1;
            return true;
        });
    }

    // Produces a semantic ranking of chunk ids, swallowing any embedding-server failure into an
    // empty list so retrieval degrades to BM25 rather than erroring. The corpus arm wins over
    // the pool re-rank whenever it is safe (Table mode, a document scope, or Auto + ticker
    // scope) — it can surface chunks BM25 never retrieved, which the pool re-rank by
    // construction cannot. A FAILED or empty corpus arm falls back to the pool re-rank over
    // whatever BM25 found before giving up: the pool path is a by-id lookup that still works in
    // exactly the cases that kill the corpus arm (a slow distance sort, a missing index), so an
    // outlier scope must never end up WORSE ranked than it was under plain Pool mode.
    private async Task<List<Guid>> RankSemantically(
        string query,
        IReadOnlyList<Chunk> bm25Pool,
        bool corpusArmSafe,
        string ticker,
        Guid? documentId,
        DocumentType documentType,
        DateOnly? startDate,
        DateOnly? endDate,
        CancellationToken cancellationToken
    )
    {
        if (corpusArmSafe)
        {
            try
            {
                var corpus = await RankCorpus(
                    query,
                    ticker,
                    documentId,
                    documentType,
                    startDate,
                    endDate,
                    cancellationToken
                );
                if (corpus.Count > 0)
                    return corpus;
            }
            catch (Exception exception)
            {
                _logger.LogWarning(
                    exception,
                    "Corpus vector arm failed or timed out; falling back to the pool re-rank"
                );
            }
        }

        try
        {
            return await RankPool(query, bm25Pool, cancellationToken);
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

using Equibles.Data;
using Equibles.Sec.Data.Models;
using Equibles.Sec.Data.Models.Chunks;
using Microsoft.EntityFrameworkCore;
using Pgvector;
using Pgvector.EntityFrameworkCore;

namespace Equibles.Sec.Repositories;

public class EmbeddingRepository : BaseRepository<Embedding>
{
    // Hard ceiling for the corpus vector search, mirroring ChunkRepository.HybridSearch: until an
    // ANN (HNSW) index exists on the vector column this is a full-corpus distance scan, and pgvector
    // doesn't check the cancellation token mid-execution — without this a slow scan pins the Npgsql
    // connection past the caller's budget (issue #1026).
    private const int CorpusSearchCommandTimeoutSeconds = 5;

    public EmbeddingRepository(EquiblesFinancialDbContext dbContext)
        : base(dbContext) { }

    public async Task<Embedding> GetByChunk(Chunk chunk)
    {
        return await GetAll().FirstOrDefaultAsync(e => e.ChunkId == chunk.Id);
    }

    public IQueryable<Embedding> GetByChunks(IEnumerable<Chunk> chunks)
    {
        var chunkIds = chunks.Select(c => c.Id).ToList();
        return GetAll().Where(e => chunkIds.Contains(e.ChunkId));
    }

    public IQueryable<Embedding> GetByModel(string model)
    {
        return GetAll().Where(e => e.Model == model);
    }

    public async Task<List<Embedding>> SearchSimilar(
        float[] queryEmbedding,
        string model,
        int maxResults = 5
    )
    {
        var queryVector = new Vector(queryEmbedding);

        return await GetAll()
            .Where(e => e.Model == model)
            .OrderBy(e => e.Vector.CosineDistance(queryVector))
            .Take(maxResults)
            .ToListAsync();
    }

    /// <summary>
    /// Corpus-wide nearest neighbours for the hybrid searcher's <see cref="VectorSource.Table"/>
    /// arm, scoped through the Chunk navigation to the same ticker/document/type/date filters BM25
    /// applies so the two arms rank over the same universe. Returns chunk ids in similarity order.
    /// </summary>
    // virtual: unit tests stub the vector seam by subclassing (no pgvector in a unit run).
    public virtual async Task<List<Guid>> SearchSimilarChunks(
        float[] queryEmbedding,
        string model,
        int maxResults,
        string ticker = null,
        Guid? documentId = null,
        DocumentType documentType = null,
        DateTime? startUtc = null,
        DateTime? endUtc = null,
        CancellationToken cancellationToken = default
    )
    {
        var queryVector = new Vector(queryEmbedding);
        var query = GetAll().Where(e => e.Model == model);

        if (ticker != null)
        {
            var loweredTicker = ticker.ToLowerInvariant();
            query = query.Where(e =>
                e.Chunk.Ticker != null && e.Chunk.Ticker.ToLower() == loweredTicker
            );
        }

        if (documentId.HasValue)
            query = query.Where(e => e.Chunk.DocumentId == documentId.Value);

        if (documentType != null)
            query = query.Where(e => e.Chunk.DocumentType == documentType);

        if (startUtc.HasValue)
            query = query.Where(e => e.Chunk.ReportingDate >= startUtc.Value);

        if (endUtc.HasValue)
            query = query.Where(e => e.Chunk.ReportingDate <= endUtc.Value);

        var originalTimeout = DbContext.Database.GetCommandTimeout();
        DbContext.Database.SetCommandTimeout(CorpusSearchCommandTimeoutSeconds);
        try
        {
            return await query
                .OrderBy(e => e.Vector.CosineDistance(queryVector))
                .Take(maxResults)
                .Select(e => e.ChunkId)
                .ToListAsync(cancellationToken);
        }
        finally
        {
            DbContext.Database.SetCommandTimeout(originalTimeout);
        }
    }

    public async Task<List<Embedding>> SearchSimilarWithThreshold(
        float[] queryEmbedding,
        string model,
        double threshold,
        int maxResults = 5
    )
    {
        var queryVector = new Vector(queryEmbedding);

        return await GetAll()
            .Where(e => e.Model == model && e.Vector.CosineDistance(queryVector) <= threshold)
            .OrderBy(e => e.Vector.CosineDistance(queryVector))
            .Take(maxResults)
            .ToListAsync();
    }
}

using Equibles.Data;
using Equibles.Sec.Data.Models.Chunks;
using Microsoft.EntityFrameworkCore;
using Pgvector;
using Pgvector.EntityFrameworkCore;

namespace Equibles.Sec.Repositories;

public class EmbeddingRepository : BaseRepository<Embedding> {
    public EmbeddingRepository(EquiblesDbContext dbContext) : base(dbContext) {
    }

    public async Task<Embedding> GetByChunk(Chunk chunk) {
        return await GetAll()
            .FirstOrDefaultAsync(e => e.ChunkId == chunk.Id);
    }

    public IQueryable<Embedding> GetByChunks(IEnumerable<Chunk> chunks) {
        var chunkIds = chunks.Select(c => c.Id).ToList();
        return GetAll()
            .Where(e => chunkIds.Contains(e.ChunkId));
    }

    public IQueryable<Embedding> GetByModel(string model) {
        return GetAll()
            .Where(e => e.Model == model);
    }

    public async Task<List<Embedding>> SearchSimilar(float[] queryEmbedding, string model, int maxResults = 5) {
        var queryVector = new Vector(queryEmbedding);

        return await GetAll()
            .Where(e => e.Model == model)
            .OrderBy(e => e.Vector.CosineDistance(queryVector))
            .Take(maxResults)
            .ToListAsync();
    }

    public async Task<List<Embedding>> SearchSimilarWithThreshold(
        float[] queryEmbedding,
        string model,
        double threshold,
        int maxResults = 5
    ) {
        var queryVector = new Vector(queryEmbedding);

        return await GetAll()
            .Where(e => e.Model == model && e.Vector.CosineDistance(queryVector) <= threshold)
            .OrderBy(e => e.Vector.CosineDistance(queryVector))
            .Take(maxResults)
            .ToListAsync();
    }
}

using Equibles.CommonStocks.Repositories;
using Equibles.Core.AutoWiring;
using Equibles.Sec.BusinessLogic.Embeddings;
using Equibles.Sec.BusinessLogic.Processing;
using Equibles.Sec.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Equibles.Sec.HostedService.Services;

[Service]
public class DocumentManager
{
    private const int DefaultLoadSize = 1024;

    private readonly DocumentRepository _documentRepository;
    private readonly ChunkRepository _chunkRepository;
    private readonly IDocumentProcessor _documentProcessor;
    private readonly EmbeddingConfig _embeddingConfig;
    private readonly int _loadSize;
    private readonly ILogger<DocumentManager> _logger;

    public DocumentManager(
        DocumentRepository documentRepository,
        ChunkRepository chunkRepository,
        IDocumentProcessor documentProcessor,
        IOptions<EmbeddingConfig> embeddingConfig,
        ILogger<DocumentManager> logger
    )
    {
        _documentRepository = documentRepository;
        _chunkRepository = chunkRepository;
        _documentProcessor = documentProcessor;
        _embeddingConfig = embeddingConfig.Value;
        _loadSize = Math.Max(DefaultLoadSize, _embeddingConfig.BatchSize);
        _logger = logger;
    }

    // Both batch loaders anti-join a huge table ("no chunks yet" / "no embeddings yet") ordered
    // by CreationTime. Unfloored, that anti-join hashes the entire table on every poll — minutes
    // of I/O each time on the chunk corpus. The caller-owned cursor floors each poll at the
    // backfill frontier so the CreationTime index seeks straight to the pending rows; the
    // rate-limited full-rescan fallback in LoadBatch catches rows left behind the frontier.

    public async Task<bool> ChunkDocumentBatch(
        BackfillCursor cursor,
        CancellationToken cancellationToken
    )
    {
        var pendingDocuments = await LoadBatch(
            floor =>
            {
                var query = _documentRepository
                    .GetAll()
                    .Include(d => d.CommonStock)
                    .Include(d => d.Content)
                    .Where(d => !d.Chunks.Any() && d.Content != null);
                if (floor is { } f)
                    query = query.Where(d => d.CreationTime >= f);
                return query
                    .OrderBy(d => d.CreationTime)
                    .Take(_loadSize)
                    .ToListAsync(cancellationToken);
            },
            cursor
        );

        if (pendingDocuments.Count == 0)
            return false;

        _logger.LogInformation("Chunking {Count} documents", pendingDocuments.Count);
        await _documentProcessor.ProcessDocuments(pendingDocuments, cancellationToken);
        cursor.Advance(pendingDocuments[^1].CreationTime);
        return true;
    }

    public async Task<bool> GenerateEmbeddingBatch(
        BackfillCursor cursor,
        CancellationToken cancellationToken
    )
    {
        if (!_embeddingConfig.IsConfigured)
            return false;

        var chunksWithoutEmbeddings = await LoadBatch(
            floor =>
            {
                var query = _chunkRepository.GetAll().Where(c => !c.Embeddings.Any());
                if (floor is { } f)
                    query = query.Where(c => c.CreationTime >= f);
                return query
                    .OrderBy(c => c.CreationTime)
                    .Take(_loadSize)
                    .ToListAsync(cancellationToken);
            },
            cursor
        );

        if (chunksWithoutEmbeddings.Count == 0)
            return false;

        _logger.LogInformation(
            "Generating embeddings for {Count} chunks",
            chunksWithoutEmbeddings.Count
        );
        await _documentProcessor.GenerateEmbeddings(chunksWithoutEmbeddings, cancellationToken);
        cursor.Advance(chunksWithoutEmbeddings[^1].CreationTime);
        return true;
    }

    // Floored batch first; when the frontier drains, at most one rate-limited full scan so
    // stragglers behind the cursor (failed items, re-queued work) are eventually retried.
    private static async Task<List<T>> LoadBatch<T>(
        Func<DateTime?, Task<List<T>>> query,
        BackfillCursor cursor
    )
    {
        if (cursor.Floor is { } floor)
        {
            var batch = await query(floor);
            if (batch.Count > 0)
                return batch;
        }

        if (!cursor.TryStartFullRescan(DateTime.UtcNow))
            return [];

        return await query(null);
    }
}

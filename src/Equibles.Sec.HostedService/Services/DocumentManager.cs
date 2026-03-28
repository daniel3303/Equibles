using Equibles.Sec.BusinessLogic.Processing;
using Equibles.Sec.BusinessLogic.Embeddings;
using Equibles.Core.AutoWiring;
using Equibles.CommonStocks.Repositories;
using Equibles.Sec.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Equibles.Sec.HostedService.Services;

[Service]
public class DocumentManager {
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
    ) {
        _documentRepository = documentRepository;
        _chunkRepository = chunkRepository;
        _documentProcessor = documentProcessor;
        _embeddingConfig = embeddingConfig.Value;
        _loadSize = Math.Max(DefaultLoadSize, _embeddingConfig.BatchSize);
        _logger = logger;
    }

    public async Task<bool> ChunkDocumentBatch(CancellationToken cancellationToken) {
        var pendingDocuments = await _documentRepository.GetAll()
            .Include(d => d.CommonStock)
            .Include(d => d.Content)
            .Where(d => !d.Chunks.Any() && d.Content != null)
            .OrderBy(d => d.CreationTime)
            .Take(_loadSize)
            .ToListAsync(cancellationToken);

        if (!pendingDocuments.Any()) return false;

        _logger.LogInformation("Chunking {Count} documents", pendingDocuments.Count);
        await _documentProcessor.ProcessDocuments(pendingDocuments, cancellationToken);
        return true;
    }

    public async Task<bool> GenerateEmbeddingBatch(CancellationToken cancellationToken) {
        if (!_embeddingConfig.IsConfigured) return false;

        var chunksWithoutEmbeddings = await _chunkRepository.GetAll()
            .Where(c => !c.Embeddings.Any())
            .OrderBy(c => c.CreationTime)
            .Take(_loadSize)
            .ToListAsync(cancellationToken);

        if (!chunksWithoutEmbeddings.Any()) return false;

        _logger.LogInformation("Generating embeddings for {Count} chunks", chunksWithoutEmbeddings.Count);
        await _documentProcessor.GenerateEmbeddings(chunksWithoutEmbeddings, cancellationToken);
        return true;
    }
}

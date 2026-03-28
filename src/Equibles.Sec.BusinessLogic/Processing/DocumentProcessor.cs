using System.Text;
using Equibles.Sec.BusinessLogic.Embeddings;
using Equibles.Core.AutoWiring;
using Equibles.Sec.Data.Models;
using Equibles.Sec.Data.Models.Chunks;
using Equibles.Sec.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pgvector;

namespace Equibles.Sec.BusinessLogic.Processing;

[Service(ServiceLifetime.Scoped, typeof(IDocumentProcessor))]
public class DocumentProcessor : IDocumentProcessor {
    private readonly ChunkRepository _chunkRepository;
    private readonly EmbeddingRepository _embeddingRepository;
    private readonly IEmbeddingClient _embeddingClient;
    private readonly ChunkingStrategy _chunkingStrategy;
    private readonly EmbeddingConfig _embeddingConfig;
    private readonly ILogger<DocumentProcessor> _logger;

    public DocumentProcessor(ChunkRepository chunkRepository,
        EmbeddingRepository embeddingRepository,
        IEmbeddingClient embeddingClient,
        ChunkingStrategy chunkingStrategy,
        IOptions<EmbeddingConfig> embeddingConfig,
        ILogger<DocumentProcessor> logger
    ) {
        _chunkRepository = chunkRepository;
        _embeddingRepository = embeddingRepository;
        _embeddingClient = embeddingClient;
        _chunkingStrategy = chunkingStrategy;
        _embeddingConfig = embeddingConfig.Value;
        _logger = logger;
    }

    public async Task ProcessDocuments(List<Document> documents, CancellationToken cancellationToken) {
        foreach (var document in documents) {
            if (cancellationToken.IsCancellationRequested) {
                _logger.LogInformation("Cancellation requested, stopping document processing");
                break;
            }

            try {
                await ChunkDocument(document);
            } catch (Exception ex) {
                _logger.LogError(ex, "Error chunking document {DocumentId}", document.Id);
            }
        }
    }

    public async Task GenerateEmbeddings(List<Chunk> chunks, CancellationToken cancellationToken) {
        // Group by document for logging
        var chunksByDocument = chunks.GroupBy(c => c.DocumentId);

        foreach (var group in chunksByDocument) {
            if (cancellationToken.IsCancellationRequested) {
                _logger.LogInformation("Cancellation requested, stopping embedding generation");
                break;
            }

            var documentChunks = group.ToList();
            try {
                await GenerateEmbeddingsForChunks(documentChunks);
                _logger.LogInformation("Generated embeddings for {Count} chunks of document {DocumentId}",
                    documentChunks.Count, group.Key);
            } catch (Exception ex) {
                _logger.LogError(ex, "Error generating embeddings for document {DocumentId}. " +
                    "Stopping batch — embedding server is likely down", group.Key);
                throw;
            }
        }
    }

    private async Task ChunkDocument(Document document) {
        if (document == null) {
            _logger.LogWarning("Document is null, skipping processing");
            return;
        }

        if (document.Chunks.Any()) {
            _logger.LogInformation("Document {DocumentId} already chunked, skipping", document.Id);
            return;
        }

        _logger.LogInformation("Chunking document {DocumentId} for {Company} ({Ticker})",
            document.Id, document.CommonStock.Name, document.CommonStock.Ticker);

        var content = GetDocumentContent(document);
        if (string.IsNullOrWhiteSpace(content)) {
            _logger.LogWarning("Document {DocumentId} has no content", document.Id);
            return;
        }

        var chunks = await CreateChunks(document, content);
        _logger.LogInformation("Created {ChunkCount} chunks for document {DocumentId}", chunks.Count, document.Id);
    }

    private string GetDocumentContent(Document document) {
        if (document.Content == null) {
            throw new Exception("Document content is null");
        }

        return Encoding.UTF8.GetString(document.Content.FileContent.Bytes);
    }

    private async Task<List<Chunk>> CreateChunks(Document document, string content) {
        var allChunks = new List<Chunk>();
        var currentTime = DateTime.UtcNow;

        var chunkInfos = _chunkingStrategy.SplitIntoChunks(content);
        for (var i = 0; i < chunkInfos.Count; i++) {
            var chunk = new Chunk {
                Document = document,
                DocumentType = document.DocumentType,
                Ticker = document.CommonStock.Ticker,
                ReportingDate = DateTime.SpecifyKind(document.ReportingDate.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc),
                Index = i,
                StartPosition = chunkInfos[i].StartPosition,
                EndPosition = chunkInfos[i].EndPosition,
                StartLineNumber = chunkInfos[i].StartLineNumber,
                Content = chunkInfos[i].Content,
                CreationTime = currentTime
            };

            _chunkRepository.Add(chunk);
            allChunks.Add(chunk);
        }

        await _chunkRepository.SaveChanges();
        return allChunks;
    }

    private async Task GenerateEmbeddingsForChunks(List<Chunk> chunks) {
        var chunkContents = chunks.Select(c => c.Content).ToList();

        var embeddings = await _embeddingClient.GenerateEmbeddings(chunkContents);

        if (embeddings.Count != chunks.Count) {
            throw new InvalidOperationException(
                $"Embedding count mismatch: expected {chunks.Count}, got {embeddings.Count}");
        }

        for (int i = 0; i < chunks.Count; i++) {
            var embedding = new Embedding {
                Chunk = chunks[i],
                Model = _embeddingConfig.ModelName,
                Vector = new Vector(embeddings[i]),
                VectorDimension = embeddings[i].Length,
                CreationTime = DateTime.UtcNow
            };

            _embeddingRepository.Add(embedding);
        }

        await _embeddingRepository.SaveChanges();
    }
}

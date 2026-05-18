using System.Text;
using Equibles.Core.AutoWiring;
using Equibles.Sec.BusinessLogic.Embeddings;
using Equibles.Sec.Data.Models;
using Equibles.Sec.Data.Models.Chunks;
using Equibles.Sec.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pgvector;

namespace Equibles.Sec.BusinessLogic.Processing;

[Service(ServiceLifetime.Scoped, typeof(IDocumentProcessor))]
public class DocumentProcessor : IDocumentProcessor
{
    private readonly ChunkRepository _chunkRepository;
    private readonly EmbeddingRepository _embeddingRepository;
    private readonly IEmbeddingClient _embeddingClient;
    private readonly ChunkingStrategy _chunkingStrategy;
    private readonly EmbeddingConfig _embeddingConfig;
    private readonly ILogger<DocumentProcessor> _logger;

    public DocumentProcessor(
        ChunkRepository chunkRepository,
        EmbeddingRepository embeddingRepository,
        IEmbeddingClient embeddingClient,
        ChunkingStrategy chunkingStrategy,
        IOptions<EmbeddingConfig> embeddingConfig,
        ILogger<DocumentProcessor> logger
    )
    {
        _chunkRepository = chunkRepository;
        _embeddingRepository = embeddingRepository;
        _embeddingClient = embeddingClient;
        _chunkingStrategy = chunkingStrategy;
        _embeddingConfig = embeddingConfig.Value;
        _logger = logger;
    }

    public async Task ProcessDocuments(
        List<Document> documents,
        CancellationToken cancellationToken
    )
    {
        foreach (var document in documents)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                _logger.LogInformation("Cancellation requested, stopping document processing");
                break;
            }

            try
            {
                await ChunkDocument(document);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error chunking document {DocumentId}", document.Id);
            }
        }
    }

    public async Task GenerateEmbeddings(List<Chunk> chunks, CancellationToken cancellationToken)
    {
        // Group by document for logging
        var chunksByDocument = chunks.GroupBy(c => c.DocumentId);

        var totalChunks = 0;
        var totalEmbedded = 0;

        foreach (var group in chunksByDocument)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                _logger.LogInformation("Cancellation requested, stopping embedding generation");
                break;
            }

            var documentChunks = group.ToList();
            totalChunks += documentChunks.Count;
            try
            {
                var embedded = await GenerateEmbeddingsForChunks(documentChunks);
                totalEmbedded += embedded;
                _logger.LogInformation(
                    "Generated embeddings for {Embedded}/{Count} chunks of document {DocumentId}",
                    embedded,
                    documentChunks.Count,
                    group.Key
                );
            }
            catch (Exception ex)
            {
                // Do not rethrow here: an unexpected failure on one document must
                // not abort embedding for the remaining documents. Log and move
                // on; the chunks stay unembedded and are retried on a later pass.
                _logger.LogWarning(
                    ex,
                    "Skipping embeddings for document {DocumentId} after an unexpected "
                        + "error; continuing with the rest",
                    group.Key
                );
            }
        }

        // Distinguish a few poison chunks (isolated above, so the backlog keeps
        // draining) from a systemic outage. If we attempted chunks but embedded
        // none, the embedding server is down: throw so the worker's base loop
        // logs it loudly, reports it, and backs off for a cycle — instead of
        // hot-looping on the same batch with zero progress and no error.
        if (totalChunks > 0 && totalEmbedded == 0)
        {
            throw new InvalidOperationException(
                $"No embeddings were produced for any of {totalChunks} chunks — "
                    + "the embedding server is likely down. Backing off this cycle."
            );
        }
    }

    private async Task ChunkDocument(Document document)
    {
        if (document == null)
        {
            _logger.LogWarning("Document is null, skipping processing");
            return;
        }

        if (document.Chunks.Any())
        {
            _logger.LogInformation("Document {DocumentId} already chunked, skipping", document.Id);
            return;
        }

        _logger.LogInformation(
            "Chunking document {DocumentId} for {Company} ({Ticker})",
            document.Id,
            document.CommonStock.Name,
            document.CommonStock.Ticker
        );

        var content = GetDocumentContent(document);
        if (string.IsNullOrWhiteSpace(content))
        {
            _logger.LogWarning("Document {DocumentId} has no content", document.Id);
            return;
        }

        var chunks = await CreateChunks(document, content);
        _logger.LogInformation(
            "Created {ChunkCount} chunks for document {DocumentId}",
            chunks.Count,
            document.Id
        );
    }

    private string GetDocumentContent(Document document)
    {
        if (document.Content == null)
        {
            throw new Exception("Document content is null");
        }

        return Encoding.UTF8.GetString(document.Content.FileContent.Bytes);
    }

    private async Task<List<Chunk>> CreateChunks(Document document, string content)
    {
        var allChunks = new List<Chunk>();
        var currentTime = DateTime.UtcNow;

        var chunkInfos = _chunkingStrategy.SplitIntoChunks(content);
        for (var i = 0; i < chunkInfos.Count; i++)
        {
            var chunk = new Chunk
            {
                Document = document,
                DocumentType = document.DocumentType,
                Ticker = document.CommonStock.Ticker,
                ReportingDate = DateTime.SpecifyKind(
                    document.ReportingDate.ToDateTime(TimeOnly.MinValue),
                    DateTimeKind.Utc
                ),
                Index = i,
                StartPosition = chunkInfos[i].StartPosition,
                EndPosition = chunkInfos[i].EndPosition,
                StartLineNumber = chunkInfos[i].StartLineNumber,
                Content = chunkInfos[i].Content,
                CreationTime = currentTime,
            };

            _chunkRepository.Add(chunk);
            allChunks.Add(chunk);
        }

        await _chunkRepository.SaveChanges();
        return allChunks;
    }

    /// <summary>
    /// Embeds the given chunks and persists the successful ones. Returns the
    /// number of embeddings actually written — entries the embedding client
    /// could not produce (null, positionally aligned to the input) are skipped,
    /// so a return of 0 means nothing in this document could be embedded.
    /// </summary>
    private async Task<int> GenerateEmbeddingsForChunks(List<Chunk> chunks)
    {
        var chunkContents = chunks.Select(c => c.Content).ToList();

        var embeddings = await _embeddingClient.GenerateEmbeddings(chunkContents);

        // embeddings is positionally aligned to chunks; an entry is null when
        // that chunk could not be embedded (e.g. the model returned a NaN
        // vector and Ollama 500'd). Skip those chunks instead of failing the
        // whole batch — they simply stay unembedded and can be retried later.
        var count = Math.Min(chunks.Count, embeddings.Count);
        var added = 0;
        for (int i = 0; i < count; i++)
        {
            if (embeddings[i] == null)
            {
                continue;
            }

            _embeddingRepository.Add(
                new Embedding
                {
                    Chunk = chunks[i],
                    Model = _embeddingConfig.ModelName,
                    Vector = new Vector(embeddings[i]),
                    VectorDimension = embeddings[i].Length,
                    CreationTime = DateTime.UtcNow,
                }
            );
            added++;
        }

        if (added > 0)
        {
            await _embeddingRepository.SaveChanges();
        }

        return added;
    }
}

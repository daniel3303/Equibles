using Equibles.CommonStocks.Data.Models;
using Equibles.IntegrationTests.Helpers;
using Equibles.Media.Data.Models;
using Equibles.Sec.Data.Models;
using Equibles.Sec.Data.Models.Chunks;
using Equibles.Sec.Repositories;
using Microsoft.EntityFrameworkCore;
using Pgvector;
using Xunit;
using File = Equibles.Media.Data.Models.File;

namespace Equibles.IntegrationTests.Sec;

/// <summary>
/// Companion to EmbeddingRepositoryTests (which pins SearchSimilar's cosine
/// ordering). `GetByModel` is the model-versioning filter that lets the
/// embedding-refresh worker delete or re-embed only the rows owned by a
/// retired model. A refactor that broadened the predicate (substring match,
/// case-insensitive comparison, dropping the filter entirely) would silently
/// mix vectors from different embedding models — they have different
/// dimensions and distance distributions, so the RAG ranker would degrade
/// without any visible failure. Pin the exact-match contract.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class EmbeddingRepositoryGetByModelFilterTests : ParadeDbMcpTestBase
{
    public EmbeddingRepositoryGetByModelFilterTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task GetByModel_ReturnsOnlyEmbeddingsExactlyMatchingModelName()
    {
        var sut = new EmbeddingRepository(DbContext);

        var stock = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = "AAPL",
            Name = "Apple Inc.",
        };
        var file = new File
        {
            Id = Guid.NewGuid(),
            Name = "10K",
            Extension = "html",
            ContentType = "text/html",
            Size = 1,
            FileContent = new FileContent { Bytes = [0x01] },
        };
        var document = new Document
        {
            Id = Guid.NewGuid(),
            CommonStockId = stock.Id,
            ContentId = file.Id,
            DocumentType = DocumentType.TenK,
            ReportingDate = new DateOnly(2024, 3, 15),
            ReportingForDate = new DateOnly(2023, 12, 31),
            LineCount = 1,
            SourceUrl = "https://example.test/filing",
        };

        // Three embeddings under three distinct model names — including a
        // model name that is a *prefix* of the target ("model-v1" vs target
        // "model-v1-large"). A substring/StartsWith regression would falsely
        // return the prefix row as well; an exact-match contract must not.
        var keepChunk = MakeChunkWithEmbedding(document, index: 0, model: "model-v1-large");
        var prefixChunk = MakeChunkWithEmbedding(document, index: 1, model: "model-v1");
        var otherChunk = MakeChunkWithEmbedding(document, index: 2, model: "different-model");

        DbContext.Set<CommonStock>().Add(stock);
        DbContext.Set<File>().Add(file);
        DbContext.Set<Document>().Add(document);
        DbContext.Set<Chunk>().AddRange(keepChunk.chunk, prefixChunk.chunk, otherChunk.chunk);
        DbContext
            .Set<Embedding>()
            .AddRange(keepChunk.embedding, prefixChunk.embedding, otherChunk.embedding);
        await DbContext.SaveChangesAsync();

        DbContext.ChangeTracker.Clear();

        var results = await sut.GetByModel("model-v1-large").ToListAsync();

        results.Should().ContainSingle();
        results[0].ChunkId.Should().Be(keepChunk.chunk.Id);
    }

    private static (Chunk chunk, Embedding embedding) MakeChunkWithEmbedding(
        Document document,
        int index,
        string model
    )
    {
        var chunk = new Chunk
        {
            Id = Guid.NewGuid(),
            DocumentId = document.Id,
            Content = $"chunk-{index}",
            Index = index,
            StartPosition = index * 100,
            EndPosition = (index + 1) * 100,
            StartLineNumber = index + 1,
            DocumentType = document.DocumentType,
            Ticker = "AAPL",
            ReportingDate = document.ReportingDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
        };
        var embedding = new Embedding
        {
            Id = Guid.NewGuid(),
            ChunkId = chunk.Id,
            Model = model,
            Vector = new Vector(new ReadOnlyMemory<float>(new[] { 1f, 0f, 0f })),
            VectorDimension = 3,
        };
        return (chunk, embedding);
    }
}

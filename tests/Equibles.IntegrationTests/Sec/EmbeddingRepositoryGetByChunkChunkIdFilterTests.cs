using Equibles.CommonStocks.Data.Models;
using Equibles.IntegrationTests.Helpers;
using Equibles.Media.Data.Models;
using Equibles.Sec.Data.Models;
using Equibles.Sec.Data.Models.Chunks;
using Equibles.Sec.Repositories;
using Pgvector;
using Xunit;
using File = Equibles.Media.Data.Models.File;

namespace Equibles.IntegrationTests.Sec;

/// <summary>
/// Sibling to EmbeddingRepositoryTests (SearchSimilar) and
/// EmbeddingRepositoryGetByModelFilterTests (model filter, PR #2211).
/// `GetByChunk` is the per-chunk lookup the embedding-refresh worker uses to
/// decide whether a chunk already has an embedding before queueing a recompute.
/// A refactor that dropped the predicate (e.g. switched to a bare
/// FirstOrDefaultAsync()) would happily return *some* embedding from the table,
/// confusing the worker into believing every chunk is already embedded — RAG
/// retrieval would degrade silently because the wrong vector indexes the chunk.
/// Pin the chunk-id-equality contract: a chunk with no embedding returns null,
/// a chunk with one embedding returns exactly that row.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class EmbeddingRepositoryGetByChunkChunkIdFilterTests : ParadeDbMcpTestBase
{
    public EmbeddingRepositoryGetByChunkChunkIdFilterTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task GetByChunk_ChunkWithoutOwnEmbedding_ReturnsNullEvenWhenOtherEmbeddingsExist()
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
            LineCount = 2,
            SourceUrl = "https://example.test/filing",
        };

        var embeddedChunk = MakeChunk(document, index: 0);
        var unembeddedChunk = MakeChunk(document, index: 1);

        var existingEmbedding = new Embedding
        {
            Id = Guid.NewGuid(),
            ChunkId = embeddedChunk.Id,
            Model = "test-model",
            Vector = new Vector(new ReadOnlyMemory<float>(new[] { 1f, 0f, 0f })),
            VectorDimension = 3,
        };

        DbContext.Set<CommonStock>().Add(stock);
        DbContext.Set<File>().Add(file);
        DbContext.Set<Document>().Add(document);
        DbContext.Set<Chunk>().AddRange(embeddedChunk, unembeddedChunk);
        DbContext.Set<Embedding>().Add(existingEmbedding);
        await DbContext.SaveChangesAsync();

        DbContext.ChangeTracker.Clear();

        var result = await sut.GetByChunk(unembeddedChunk);

        result
            .Should()
            .BeNull(
                "the chunk has no embedding row, so the lookup must not fall back to "
                    + "an unrelated embedding from another chunk"
            );
    }

    private static Chunk MakeChunk(Document document, int index) =>
        new()
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
}

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
/// Sibling to <see cref="EmbeddingRepositoryTests"/>. That test pins
/// <c>SearchSimilar</c>; this pins <c>SearchSimilarWithThreshold</c> — the
/// variant the RAG pipeline calls when it wants to exclude irrelevant chunks
/// from the context window. The threshold filter is load-bearing: a regression
/// that dropped <c>CosineDistance &lt;= threshold</c> would silently return up
/// to <c>maxResults</c> embeddings regardless of similarity, padding RAG with
/// noise.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class EmbeddingRepositorySearchWithThresholdTests : ParadeDbMcpTestBase
{
    private const string Model = "test-embedding-model-v1";

    public EmbeddingRepositorySearchWithThresholdTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task SearchSimilarWithThreshold_TightThresholdExcludesFarVectors_ReturnsOnlyClosest()
    {
        var sut = new EmbeddingRepository(DbContext);

        var stock = new CommonStock { Ticker = "AAPL", Name = "Apple Inc." };
        var file = new File
        {
            Name = "10K",
            Extension = "html",
            ContentType = "text/html",
            Size = 2,
            FileContent = new FileContent { Bytes = new byte[] { 0x01, 0x02 } },
        };
        var document = new Document
        {
            CommonStockId = stock.Id,
            ContentId = file.Id,
            DocumentType = DocumentType.TenK,
            ReportingDate = new DateOnly(2024, 3, 15),
            ReportingForDate = new DateOnly(2023, 12, 31),
            LineCount = 1,
            SourceUrl = "https://example.test/filing",
        };

        // X-axis vector: cosine distance to query [1, 0, 0] is 0. Z-axis: distance
        // is 1. A threshold of 0.5 must include X and exclude Z.
        var chunkX = MakeChunk(document, "x-axis", 0);
        var chunkZ = MakeChunk(document, "z-axis", 1);
        var embedX = MakeEmbedding(chunkX.Id, new[] { 1f, 0f, 0f });
        var embedZ = MakeEmbedding(chunkZ.Id, new[] { 0f, 0f, 1f });

        DbContext.Set<CommonStock>().Add(stock);
        DbContext.Set<File>().Add(file);
        DbContext.Set<Document>().Add(document);
        DbContext.Set<Chunk>().AddRange(chunkX, chunkZ);
        DbContext.Set<Embedding>().AddRange(embedX, embedZ);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        var queryVector = new[] { 1f, 0f, 0f };
        var results = await sut.SearchSimilarWithThreshold(
            queryVector,
            Model,
            threshold: 0.5,
            maxResults: 5
        );

        // The threshold is the entire test — Z must NOT appear despite maxResults=5.
        results.Should().ContainSingle();
        results[0].ChunkId.Should().Be(chunkX.Id);
    }

    private static Chunk MakeChunk(Document document, string content, int index) =>
        new()
        {
            DocumentId = document.Id,
            Content = content,
            Index = index,
            StartPosition = index * 100,
            EndPosition = (index + 1) * 100,
            StartLineNumber = index + 1,
            DocumentType = document.DocumentType,
            Ticker = "AAPL",
            ReportingDate = document.ReportingDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
        };

    private static Embedding MakeEmbedding(Guid chunkId, float[] vec) =>
        new()
        {
            ChunkId = chunkId,
            Model = Model,
            Vector = new Vector(new ReadOnlyMemory<float>(vec)),
            VectorDimension = vec.Length,
        };
}

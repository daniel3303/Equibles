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
/// <see cref="EmbeddingRepository.SearchSimilar"/> is what the production RAG pipeline calls
/// to rank chunks against a query vector — it relies on EF Core translating
/// <see cref="Vector.CosineDistance"/> into a <c>vector &lt;=&gt; vector</c> Postgres
/// operator and on <c>OrderBy(...).Take(n)</c> pushing the sort + limit into SQL so pgvector
/// can pick a vector index. The rest of the suite only exercises this code path with
/// NSubstituted repositories (see <see cref="Sec.RagManagerTests"/>), so a regression in the
/// Pgvector.EntityFrameworkCore translation — or in our pgvector extension wiring inside
/// <c>UseVector()</c> — would silently flip RAG results in production with nothing in the
/// test suite catching it.
///
/// The test seeds three embeddings against unit basis vectors so the expected order is
/// arithmetic, not approximation: a query vector tilted toward the first axis must rank
/// e[1,0,0] above e[0,1,0] above e[0,0,1]. Same Model on all three forces the filter clause
/// to run against real data instead of short-circuiting on an empty result set.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class EmbeddingRepositoryTests : ParadeDbMcpTestBase {
    private const string Model = "test-embedding-model-v1";

    public EmbeddingRepositoryTests(ParadeDbFixture fixture) : base(fixture) { }

    [Fact]
    public async Task SearchSimilar_OrdersByCosineDistanceFromQueryVector() {
        var sut = new EmbeddingRepository(DbContext);

        var stock = new CommonStock { Id = Guid.NewGuid(), Ticker = "AAPL", Name = "Apple Inc." };
        var file = new File {
            Id = Guid.NewGuid(),
            Name = "10K",
            Extension = "html",
            ContentType = "text/html",
            Size = 2,
            FileContent = new FileContent { Bytes = [0x01, 0x02] },
        };
        var document = new Document {
            Id = Guid.NewGuid(),
            CommonStockId = stock.Id,
            ContentId = file.Id,
            DocumentType = DocumentType.TenK,
            ReportingDate = new DateOnly(2024, 3, 15),
            ReportingForDate = new DateOnly(2023, 12, 31),
            LineCount = 1,
            SourceUrl = "https://example.test/filing",
        };

        // Three unit-basis vectors so the expected cosine ordering is unambiguous:
        // a query of [0.9, 0.1, 0] sits closest to the X axis, then the Y axis, then Z.
        var chunkX = MakeChunkWithEmbedding(document, content: "x-axis", index: 0,
            vector: new Vector(new ReadOnlyMemory<float>(new[] { 1f, 0f, 0f })));
        var chunkY = MakeChunkWithEmbedding(document, content: "y-axis", index: 1,
            vector: new Vector(new ReadOnlyMemory<float>(new[] { 0f, 1f, 0f })));
        var chunkZ = MakeChunkWithEmbedding(document, content: "z-axis", index: 2,
            vector: new Vector(new ReadOnlyMemory<float>(new[] { 0f, 0f, 1f })));

        DbContext.Set<CommonStock>().Add(stock);
        DbContext.Set<File>().Add(file);
        DbContext.Set<Document>().Add(document);
        DbContext.Set<Chunk>().AddRange(chunkX.chunk, chunkY.chunk, chunkZ.chunk);
        DbContext.Set<Embedding>().AddRange(chunkX.embedding, chunkY.embedding, chunkZ.embedding);
        await DbContext.SaveChangesAsync();

        // Foreign-key-aware seeding via navigation properties leaves the chunks in the
        // change tracker; clearing forces SearchSimilar to round-trip through pgvector
        // rather than serving from the in-memory cache.
        DbContext.ChangeTracker.Clear();

        var queryVector = new[] { 0.9f, 0.1f, 0f };

        var results = await sut.SearchSimilar(queryVector, Model, maxResults: 3);

        results.Should().HaveCount(3);
        results[0].ChunkId.Should().Be(chunkX.chunk.Id, "the x-axis vector is the nearest to the query");
        results[1].ChunkId.Should().Be(chunkY.chunk.Id, "the y-axis vector is second-nearest");
        results[2].ChunkId.Should().Be(chunkZ.chunk.Id, "the z-axis vector is the farthest (orthogonal to the query)");
    }

    private static (Chunk chunk, Embedding embedding) MakeChunkWithEmbedding(
        Document document, string content, int index, Vector vector
    ) {
        var chunk = new Chunk {
            Id = Guid.NewGuid(),
            DocumentId = document.Id,
            Content = content,
            Index = index,
            StartPosition = index * 100,
            EndPosition = (index + 1) * 100,
            StartLineNumber = index + 1,
            DocumentType = document.DocumentType,
            Ticker = "AAPL",
            // Npgsql's "timestamp with time zone" mapping rejects DateTimeKind.Unspecified,
            // and DateOnly.ToDateTime defaults to Unspecified — force UTC explicitly.
            ReportingDate = document.ReportingDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
        };
        var embedding = new Embedding {
            Id = Guid.NewGuid(),
            ChunkId = chunk.Id,
            Model = Model,
            Vector = vector,
            VectorDimension = 3,
        };
        return (chunk, embedding);
    }
}

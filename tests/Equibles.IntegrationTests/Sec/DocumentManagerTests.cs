using Equibles.CommonStocks.Data.Models;
using Equibles.IntegrationTests.Helpers;
using Equibles.Media.Data.Models;
using Equibles.Sec.BusinessLogic.Embeddings;
using Equibles.Sec.BusinessLogic.Processing;
using Equibles.Sec.Data.Models;
using Equibles.Sec.Data.Models.Chunks;
using Equibles.Sec.HostedService.Services;
using Equibles.Sec.Repositories;
using Microsoft.Extensions.Options;
using NSubstitute;
using Pgvector;
using Xunit;
using File = Equibles.Media.Data.Models.File;

namespace Equibles.IntegrationTests.Sec;

/// <summary>
/// The unit-tier <c>DocumentManagerTests</c> in <c>Equibles.UnitTests.Sec</c> explicitly
/// leaves <see cref="DocumentManager.ChunkDocumentBatch"/> uncovered (see its XML doc):
/// the query <c>.Include(d =&gt; d.Chunks).Where(d =&gt; !d.Chunks.Any() &amp;&amp; d.Content != null)</c>
/// only behaves correctly against real Postgres — EF Core's InMemory provider has
/// different semantics for navigation-property predicates, so a regression in how that
/// "pending document" query is shaped would slip past the unit suite. With a real
/// ParadeDB container, this test pins the production filter: a document that already
/// has chunks must be excluded, and a document without content must also be excluded —
/// so the Phase 1 worker only chunks documents that genuinely need it.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class DocumentManagerTests : ParadeDbMcpTestBase {
    private readonly IDocumentProcessor _processor = Substitute.For<IDocumentProcessor>();

    public DocumentManagerTests(ParadeDbFixture fixture) : base(fixture) { }

    [Fact]
    public async Task ChunkDocumentBatch_PendingDocuments_PassesOnlyContent_ChunklessDocumentsToProcessor() {
        var stock = new CommonStock { Id = Guid.NewGuid(), Ticker = "AAPL", Name = "Apple Inc." };

        // Pending: has Content, no Chunks — must be picked up.
        var pendingFile = MakeFile();
        var pendingDoc = MakeDocument(stock, pendingFile, contentId: pendingFile.Id, createdAt: DateTime.UtcNow.AddMinutes(-5));

        // Already chunked: has Content AND at least one Chunk — must be excluded by
        // the !d.Chunks.Any() predicate so the worker doesn't re-chunk it on every tick.
        var chunkedFile = MakeFile();
        var chunkedDoc = MakeDocument(stock, chunkedFile, contentId: chunkedFile.Id, createdAt: DateTime.UtcNow.AddMinutes(-10));
        var existingChunk = new Chunk {
            Id = Guid.NewGuid(),
            DocumentId = chunkedDoc.Id,
            Content = "already chunked content",
            Index = 0,
            StartPosition = 0,
            EndPosition = 10,
            StartLineNumber = 1,
            DocumentType = chunkedDoc.DocumentType,
            Ticker = "AAPL",
            ReportingDate = chunkedDoc.ReportingDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
        };

        DbContext.Set<CommonStock>().Add(stock);
        DbContext.Set<File>().AddRange(pendingFile, chunkedFile);
        DbContext.Set<Document>().AddRange(pendingDoc, chunkedDoc);
        DbContext.Set<Chunk>().Add(existingChunk);
        await DbContext.SaveChangesAsync();
        // Clear the tracker so the next query genuinely round-trips through Postgres
        // rather than serving Chunks from the in-memory cache.
        DbContext.ChangeTracker.Clear();

        var sut = new DocumentManager(
            new DocumentRepository(DbContext),
            new ChunkRepository(DbContext),
            _processor,
            Options.Create(new EmbeddingConfig { Enabled = false }),
            NullLogger<DocumentManager>());

        var workDone = await sut.ChunkDocumentBatch(CancellationToken.None);

        workDone.Should().BeTrue("the worker reports that pending documents were handed off to the processor");

        var passed = _processor.ReceivedCalls()
            .Single(c => c.GetMethodInfo().Name == nameof(IDocumentProcessor.ProcessDocuments))
            .GetArguments()[0] as IReadOnlyCollection<Document>;

        passed.Should().NotBeNull();
        passed!.Select(d => d.Id).Should()
            .ContainSingle(id => id == pendingDoc.Id,
                "only the chunkless-with-content document survives the !Chunks.Any() && Content != null filter");
    }

    [Fact]
    public async Task GenerateEmbeddingBatch_PendingChunks_PassesOnlyChunks_WithoutEmbeddingsToProcessor() {
        // Parallel concern to ChunkDocumentBatch: the Phase 2 worker query
        // .Where(c => !c.Embeddings.Any()) must filter on a navigation collection,
        // which only behaves correctly against real Postgres. The unit-tier
        // DocumentManagerTests exercises only the IsConfigured guard clauses for
        // this method — the actual query is exclusively pinned here.
        var stock = new CommonStock { Id = Guid.NewGuid(), Ticker = "AAPL", Name = "Apple Inc." };
        var file = MakeFile();
        var document = MakeDocument(stock, file, contentId: file.Id, createdAt: DateTime.UtcNow.AddMinutes(-5));

        var pendingChunk = MakeChunk(document, content: "needs embedding", index: 0, createdAt: DateTime.UtcNow.AddMinutes(-3));
        var embeddedChunk = MakeChunk(document, content: "already embedded", index: 1, createdAt: DateTime.UtcNow.AddMinutes(-4));
        var existingEmbedding = new Embedding {
            Id = Guid.NewGuid(),
            ChunkId = embeddedChunk.Id,
            Model = "test-model",
            Vector = new Vector(new ReadOnlyMemory<float>(new[] { 1f, 0f, 0f })),
            VectorDimension = 3,
        };

        DbContext.Set<CommonStock>().Add(stock);
        DbContext.Set<File>().Add(file);
        DbContext.Set<Document>().Add(document);
        DbContext.Set<Chunk>().AddRange(pendingChunk, embeddedChunk);
        DbContext.Set<Embedding>().Add(existingEmbedding);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        var sut = new DocumentManager(
            new DocumentRepository(DbContext),
            new ChunkRepository(DbContext),
            _processor,
            // IsConfigured is computed from Enabled + BaseUrl + ModelName; without these
            // the guard returns false before the query runs and the test pins nothing.
            Options.Create(new EmbeddingConfig {
                Enabled = true,
                BaseUrl = "http://localhost:11434",
                ModelName = "test-model",
            }),
            NullLogger<DocumentManager>());

        var workDone = await sut.GenerateEmbeddingBatch(CancellationToken.None);

        workDone.Should().BeTrue("the worker reports that embedding-less chunks were handed off to the processor");

        var passed = _processor.ReceivedCalls()
            .Single(c => c.GetMethodInfo().Name == nameof(IDocumentProcessor.GenerateEmbeddings))
            .GetArguments()[0] as IReadOnlyCollection<Chunk>;

        passed.Should().NotBeNull();
        passed!.Select(c => c.Id).Should()
            .ContainSingle(id => id == pendingChunk.Id,
                "only the embedding-less chunk survives the !c.Embeddings.Any() filter");
    }

    private static Chunk MakeChunk(Document document, string content, int index, DateTime createdAt) => new() {
        Id = Guid.NewGuid(),
        DocumentId = document.Id,
        Content = content,
        Index = index,
        StartPosition = index * 100,
        EndPosition = (index + 1) * 100,
        StartLineNumber = index + 1,
        DocumentType = document.DocumentType,
        Ticker = "AAPL",
        ReportingDate = document.ReportingDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
        CreationTime = createdAt,
    };

    private static File MakeFile() => new() {
        Id = Guid.NewGuid(),
        Name = "filing",
        Extension = "html",
        ContentType = "text/html",
        Size = 2,
        FileContent = new FileContent { Bytes = [0x01, 0x02] },
    };

    private static Document MakeDocument(CommonStock stock, File file, Guid contentId, DateTime createdAt) => new() {
        Id = Guid.NewGuid(),
        CommonStockId = stock.Id,
        ContentId = contentId,
        DocumentType = DocumentType.TenK,
        ReportingDate = new DateOnly(2024, 3, 15),
        ReportingForDate = new DateOnly(2023, 12, 31),
        LineCount = 1,
        SourceUrl = "https://example.test/filing",
        CreationTime = createdAt,
    };
}

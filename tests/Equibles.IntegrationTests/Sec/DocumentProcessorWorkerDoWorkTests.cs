using System.Reflection;
using Equibles.CommonStocks.Data.Models;
using Equibles.Errors.BusinessLogic;
using Equibles.IntegrationTests.Helpers;
using Equibles.Media.Data.Models;
using Equibles.Sec.BusinessLogic.Embeddings;
using Equibles.Sec.BusinessLogic.Processing;
using Equibles.Sec.Data.Models;
using Equibles.Sec.Data.Models.Chunks;
using Equibles.Sec.HostedService;
using Equibles.Sec.HostedService.Services;
using Equibles.Sec.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;
using File = Equibles.Media.Data.Models.File;

namespace Equibles.IntegrationTests.Sec;

/// <summary>
/// <see cref="DocumentProcessorWorker.DoWork"/> through the real scope and database harness: a
/// cycle chunks what is pending before every embedding batch and ends when no embedding is left
/// to generate.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class DocumentProcessorWorkerDoWorkTests : ParadeDbMcpTestBase
{
    public DocumentProcessorWorkerDoWorkTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task DoWork_NoPendingDocumentsAndEmbeddingsDisabled_EndsTheCycle()
    {
        var documentManager = new DocumentManager(
            new DocumentRepository(DbContext),
            new ChunkRepository(DbContext),
            new BackfillStateRepository(DbContext),
            Substitute.For<IDocumentProcessor>(),
            Options.Create(new EmbeddingConfig { Enabled = false }),
            Substitute.For<ILogger<DocumentManager>>()
        );

        var scopeFactory = ServiceScopeSubstitute.Create(
            (typeof(DocumentManager), documentManager)
        );

        var worker = new DocumentProcessorWorker(
            Substitute.For<ILogger<DocumentProcessorWorker>>(),
            scopeFactory,
            new ErrorReporter(
                Substitute.For<IServiceScopeFactory>(),
                Substitute.For<ILogger<ErrorReporter>>()
            )
        );

        var doWork = typeof(DocumentProcessorWorker).GetMethod(
            "DoWork",
            BindingFlags.NonPublic | BindingFlags.Instance
        );

        // Must complete without throwing: nothing to chunk, and embeddings are not configured.
        await (Task)doWork.Invoke(worker, [CancellationToken.None]);
    }

    // Generating every pending embedding before chunking again kept a filing that arrived
    // meanwhile out of search for the length of the embedding backlog.
    [Fact]
    public async Task DoWork_DocumentArrivingWhileEmbeddingsAreGenerated_IsChunkedBeforeTheNextEmbeddingBatch()
    {
        EquityIssuer stock = Equibles.TestSupport.EquityIssuerSeed.Create(
            Id: Guid.NewGuid(),
            Ticker: "AAPL",
            Name: "Apple Inc."
        );
        var file = MakeFile();
        var chunked = MakeDocument(stock, file, DateTime.UtcNow.AddMinutes(-5));
        chunked.ChunkedAt = DateTime.UtcNow.AddMinutes(-4);
        DbContext.Set<EquityIssuer>().Add(stock);
        DbContext.Set<File>().Add(file);
        DbContext.Set<Document>().Add(chunked);
        DbContext
            .Set<Chunk>()
            .Add(
                new Chunk
                {
                    Id = Guid.NewGuid(),
                    DocumentId = chunked.Id,
                    Content = "needs embedding",
                    Index = 0,
                    StartPosition = 0,
                    EndPosition = 100,
                    StartLineNumber = 1,
                    DocumentType = chunked.DocumentType,
                    Ticker = "AAPL",
                    ReportingDate = chunked.ReportingDate.ToDateTime(
                        TimeOnly.MinValue,
                        DateTimeKind.Utc
                    ),
                    CreationTime = DateTime.UtcNow.AddMinutes(-4),
                }
            );
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        var steps = new List<string>();
        using var stop = new CancellationTokenSource();
        var processor = Substitute.For<IDocumentProcessor>();
        // The first batch leaves its chunk without an embedding, so a second batch is due; a
        // filing arrives while it runs. The second batch ends the test.
        processor
            .GenerateEmbeddings(Arg.Any<List<Chunk>>(), Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                steps.Add("embed");
                if (steps.Count(step => step == "embed") > 1)
                {
                    await stop.CancelAsync();
                    return;
                }
                var arrivedFile = MakeFile();
                DbContext.Set<File>().Add(arrivedFile);
                DbContext.Set<Document>().Add(MakeDocument(stock, arrivedFile, DateTime.UtcNow));
                await DbContext.SaveChangesAsync();
            });
        processor
            .ProcessDocument(Arg.Any<Document>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                steps.Add("chunk");
                call.Arg<Document>().ChunkedAt = DateTime.UtcNow;
                await DbContext.SaveChangesAsync();
            });
        var documentManager = new DocumentManager(
            new DocumentRepository(DbContext),
            new ChunkRepository(DbContext),
            new BackfillStateRepository(DbContext),
            processor,
            Options.Create(
                new EmbeddingConfig
                {
                    Enabled = true,
                    BaseUrl = "http://localhost:11434",
                    ModelName = "test-model",
                }
            ),
            Substitute.For<ILogger<DocumentManager>>()
        );
        var worker = new DocumentProcessorWorker(
            Substitute.For<ILogger<DocumentProcessorWorker>>(),
            ServiceScopeSubstitute.Create((typeof(DocumentManager), documentManager)),
            new ErrorReporter(
                Substitute.For<IServiceScopeFactory>(),
                Substitute.For<ILogger<ErrorReporter>>()
            )
        );
        var doWork = typeof(DocumentProcessorWorker).GetMethod(
            "DoWork",
            BindingFlags.NonPublic | BindingFlags.Instance
        );

        await (Task)doWork.Invoke(worker, [stop.Token]);

        steps.Take(2).Should().Equal("embed", "chunk");
    }

    private static File MakeFile() =>
        new()
        {
            Id = Guid.NewGuid(),
            Name = "filing",
            Extension = "html",
            ContentType = "text/html",
            Size = 2,
            FileContent = new FileContent { Bytes = [0x01, 0x02] },
        };

    private static Document MakeDocument(EquityIssuer stock, File file, DateTime createdAt) =>
        new()
        {
            Id = Guid.NewGuid(),
            EquityIssuerId = stock.Id,
            ContentId = file.Id,
            DocumentType = DocumentType.TenK,
            ReportingDate = new DateOnly(2024, 3, 15),
            ReportingForDate = new DateOnly(2023, 12, 31),
            LineCount = 1,
            SourceUrl = "https://example.test/filing",
            CreationTime = createdAt,
        };
}

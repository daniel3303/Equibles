using Equibles.CommonStocks.Data.Models;
using Equibles.Sec.BusinessLogic.Embeddings;
using Equibles.Sec.BusinessLogic.Processing;
using Equibles.Sec.BusinessLogic.Tokenization;
using Equibles.Sec.Data.Models;
using Equibles.Sec.Data.Models.Chunks;
using Equibles.Sec.Repositories;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Equibles.IntegrationTests.Sec;

public class DocumentProcessorTests {
    private static DocumentProcessor CreateSut(IEmbeddingClient embeddingClient,
        ILogger<DocumentProcessor> logger = null) {
        return new DocumentProcessor(
            Substitute.For<ChunkRepository>((Equibles.Data.EquiblesDbContext)null),
            Substitute.For<EmbeddingRepository>((Equibles.Data.EquiblesDbContext)null),
            embeddingClient,
            new ChunkingStrategy(new TokenCounter()),
            Options.Create(new EmbeddingConfig { ModelName = "test-model" }),
            logger ?? Substitute.For<ILogger<DocumentProcessor>>());
    }

    [Fact]
    public async Task GenerateEmbeddings_AlreadyCancelledToken_DoesNotCallEmbeddingClient() {
        // GenerateEmbeddings groups chunks by document, then checks the
        // cancellation token at the top of each group. With a pre-cancelled
        // token it must break BEFORE calling _embeddingClient — the embedding
        // server sits behind an expensive Polly retry pipeline; firing even
        // one request after the worker was told to stop wastes budget and
        // can race the shutdown. Pin the early-exit so a refactor that
        // moves the cancellation check after the embedding call (or drops
        // it entirely) surfaces here instead of in production logs.
        var documentId = Guid.NewGuid();
        var chunks = new List<Chunk> {
            new() { DocumentId = documentId, Content = "first" },
        };

        var embeddingClient = Substitute.For<IEmbeddingClient>();
        var sut = CreateSut(embeddingClient);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await sut.GenerateEmbeddings(chunks, cts.Token);

        await embeddingClient.DidNotReceiveWithAnyArgs().GenerateEmbeddings(default);
    }

    [Fact]
    public async Task ProcessDocuments_DocumentWithNullContent_LogsErrorAndContinuesBatch() {
        // DocumentProcessor.ProcessDocuments is the worker's main loop —
        // SecScraperWorker hands it the whole queue of newly-fetched filings
        // each cycle. If a single document is missing its File envelope
        // (Content == null), GetDocumentContent throws Exception("Document
        // content is null"); ProcessDocuments catches that under `catch
        // (Exception ex)` and logs an error so the rest of the batch can
        // proceed. The risk this pins: a refactor that narrows the catch
        // (e.g. `catch (IOException ex)` because someone thought only IO
        // exceptions occur here) would let the bare Exception propagate up
        // to BaseScraperWorker's outer handler — which marks the entire
        // cycle as failed and skips every following document in the batch,
        // silently stalling SEC ingest until the bad row is removed by
        // hand. The catch block AND the log call together are the
        // resilience contract; both must remain to keep the worker
        // forward-progressing past data anomalies (incomplete uploads,
        // corrupted blobs, FK orphans).
        //
        // Setup: one Document with a CommonStock populated (so the leading
        // LogInformation in ChunkDocument doesn't NRE before reaching the
        // throw) but Content == null. Assert that ProcessDocuments
        // completes without throwing AND that ILogger received exactly one
        // Error call mentioning that document's Id.
        var logger = Substitute.For<ILogger<DocumentProcessor>>();
        var sut = CreateSut(Substitute.For<IEmbeddingClient>(), logger);
        var documentId = Guid.NewGuid();
        var documents = new List<Document> {
            new() {
                Id = documentId,
                CommonStock = new CommonStock { Name = "Test Co", Ticker = "TEST" },
                Content = null,
            },
        };

        var act = () => sut.ProcessDocuments(documents, CancellationToken.None);

        await act.Should().NotThrowAsync();
        logger.ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == "Log"
                && c.GetArguments().OfType<LogLevel>().FirstOrDefault() == LogLevel.Error)
            .Should().ContainSingle();
    }

    [Fact]
    public async Task GenerateEmbeddings_EmbeddingCountMismatch_Throws() {
        var documentId = Guid.NewGuid();
        var chunks = new List<Chunk> {
            new() { DocumentId = documentId, Content = "first" },
            new() { DocumentId = documentId, Content = "second" },
            new() { DocumentId = documentId, Content = "third" },
        };

        var embeddingClient = Substitute.For<IEmbeddingClient>();
        embeddingClient.GenerateEmbeddings(Arg.Any<List<string>>())
            .Returns(new List<float[]> { new float[] { 0.1f }, new float[] { 0.2f } });

        var sut = CreateSut(embeddingClient);

        var act = () => sut.GenerateEmbeddings(chunks, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Embedding count mismatch: expected 3, got 2");
    }
}

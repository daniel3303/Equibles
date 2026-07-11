using Equibles.CommonStocks.Data.Models;
using Equibles.Media.BusinessLogic;
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

public class DocumentProcessorTests
{
    private static DocumentProcessor CreateSut(
        IEmbeddingClient embeddingClient,
        ILogger<DocumentProcessor> logger = null,
        IFileManager fileManager = null
    )
    {
        return new DocumentProcessor(
            Substitute.For<ChunkRepository>((Equibles.Data.EquiblesFinancialDbContext)null),
            Substitute.For<EmbeddingRepository>((Equibles.Data.EquiblesFinancialDbContext)null),
            embeddingClient,
            new ChunkingStrategy(new TokenCounter()),
            Options.Create(new EmbeddingConfig { ModelName = "test-model" }),
            fileManager ?? Substitute.For<IFileManager>(),
            logger ?? Substitute.For<ILogger<DocumentProcessor>>()
        );
    }

    [Fact]
    public async Task GenerateEmbeddings_AlreadyCancelledToken_DoesNotCallEmbeddingClient()
    {
        // GenerateEmbeddings groups chunks by document, then checks the
        // cancellation token at the top of each group. With a pre-cancelled
        // token it must break BEFORE calling _embeddingClient — the embedding
        // server sits behind an expensive Polly retry pipeline; firing even
        // one request after the worker was told to stop wastes budget and
        // can race the shutdown. Pin the early-exit so a refactor that
        // moves the cancellation check after the embedding call (or drops
        // it entirely) surfaces here instead of in production logs.
        var documentId = Guid.NewGuid();
        var chunks = new List<Chunk>
        {
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
    public async Task ProcessDocuments_DocumentWithNullContent_LogsErrorAndContinuesBatch()
    {
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
        // Setup: one Document with Content == null plus one healthy document
        // (so the batch makes progress and the all-fail guard stays quiet).
        // Assert that ProcessDocuments completes without throwing AND that
        // ILogger received exactly one Error call for the bad document.
        var logger = Substitute.For<ILogger<DocumentProcessor>>();
        var fileManager = Substitute.For<IFileManager>();
        fileManager
            .GetContent(Arg.Any<Equibles.Media.Data.Models.File>())
            .Returns(System.Text.Encoding.UTF8.GetBytes("real filing content"));
        var sut = CreateSut(Substitute.For<IEmbeddingClient>(), logger, fileManager);
        var documentId = Guid.NewGuid();
        var documents = new List<Document>
        {
            new()
            {
                Id = documentId,
                CommonStock = new CommonStock { Name = "Test Co", Ticker = "TEST" },
                Content = null,
            },
            new()
            {
                Id = Guid.NewGuid(),
                DocumentType = DocumentType.TenK,
                CommonStock = new CommonStock { Name = "Good Co", Ticker = "GOOD" },
                Content = new Equibles.Media.Data.Models.File(),
            },
        };

        var act = () => sut.ProcessDocuments(documents, CancellationToken.None);

        await act.Should().NotThrowAsync();
        logger
            .ReceivedCalls()
            .Where(c =>
                c.GetMethodInfo().Name == "Log"
                && c.GetArguments().OfType<LogLevel>().FirstOrDefault() == LogLevel.Error
            )
            .Should()
            .ContainSingle();
    }

    [Fact]
    public async Task ProcessDocuments_EveryDocumentFails_ThrowsSoTheWorkerCycleFaults()
    {
        // Sibling guard to GenerateEmbeddings' "no embeddings produced" throw: isolated
        // failures keep the batch draining, but a batch where NOTHING could be chunked
        // (blob store unreachable, every document's content missing) must fault the cycle
        // so the worker's error ladder backs off and eventually reports — instead of the
        // batch dissolving into per-document error logs while the pending count sits
        // frozen with no surfaced error (the #4143 starvation signature).
        var sut = CreateSut(Substitute.For<IEmbeddingClient>());
        var documents = new List<Document>
        {
            new()
            {
                Id = Guid.NewGuid(),
                CommonStock = new CommonStock { Name = "Test Co", Ticker = "TEST" },
                Content = null,
            },
            new()
            {
                Id = Guid.NewGuid(),
                CommonStock = new CommonStock { Name = "Other Co", Ticker = "OTHR" },
                Content = null,
            },
        };

        var act = () => sut.ProcessDocuments(documents, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*No chunks*");
    }

    [Fact]
    public async Task ProcessDocuments_OnlyWhitespaceContent_CountsAsFailureAndThrows()
    {
        // A document whose content loads but is blank can never gain chunks — it must count
        // as a failure for the all-fail guard, not as quiet success, or a batch of such
        // documents would keep the guard silent while producing nothing.
        var fileManager = Substitute.For<IFileManager>();
        fileManager
            .GetContent(Arg.Any<Equibles.Media.Data.Models.File>())
            .Returns(System.Text.Encoding.UTF8.GetBytes("   \n\t  "));
        var sut = CreateSut(Substitute.For<IEmbeddingClient>(), fileManager: fileManager);
        var documents = new List<Document>
        {
            new()
            {
                Id = Guid.NewGuid(),
                DocumentType = DocumentType.TenK,
                CommonStock = new CommonStock { Name = "Test Co", Ticker = "TEST" },
                Content = new Equibles.Media.Data.Models.File(),
            },
        };

        var act = () => sut.ProcessDocuments(documents, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*No chunks*");
    }

    private static (DocumentProcessor Sut, EmbeddingRepository Repo) CreateSutWithRepo(
        IEmbeddingClient embeddingClient
    )
    {
        var embeddingRepository = Substitute.For<EmbeddingRepository>(
            (Equibles.Data.EquiblesFinancialDbContext)null
        );
        var sut = new DocumentProcessor(
            Substitute.For<ChunkRepository>((Equibles.Data.EquiblesFinancialDbContext)null),
            embeddingRepository,
            embeddingClient,
            new ChunkingStrategy(new TokenCounter()),
            Options.Create(new EmbeddingConfig { ModelName = "test-model" }),
            Substitute.For<IFileManager>(),
            Substitute.For<ILogger<DocumentProcessor>>()
        );
        return (sut, embeddingRepository);
    }

    private static List<Embedding> AddedEmbeddings(EmbeddingRepository repo) =>
        repo.ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == nameof(EmbeddingRepository.Add))
            .Select(c => (Embedding)c.GetArguments()[0])
            .ToList();

    [Fact]
    public async Task GenerateEmbeddings_FewerEmbeddingsThanChunks_PersistsAlignedSubsetWithoutThrowing()
    {
        // PR #866 superseded the old `if (embeddings.Count != chunks.Count) throw`
        // guard. The contract is now tolerant: GenerateEmbeddingsForChunks aligns
        // on `Math.Min(chunks.Count, embeddings.Count)`, persists the aligned
        // prefix, and leaves the rest unembedded for a later pass — a count
        // mismatch is no longer fatal because one short response must not abort
        // a document or hot-loop the worker.
        //
        // Too-low case: 3 chunks, 2 embeddings → the first 2 chunks are
        // persisted, the 3rd is silently skipped, and nothing throws.
        var documentId = Guid.NewGuid();
        var chunks = new List<Chunk>
        {
            new() { DocumentId = documentId, Content = "first" },
            new() { DocumentId = documentId, Content = "second" },
            new() { DocumentId = documentId, Content = "third" },
        };

        var embeddingClient = Substitute.For<IEmbeddingClient>();
        embeddingClient
            .GenerateEmbeddings(Arg.Any<List<string>>())
            .Returns(new List<float[]> { new float[] { 0.1f }, new float[] { 0.2f } });

        var (sut, repo) = CreateSutWithRepo(embeddingClient);

        var act = () => sut.GenerateEmbeddings(chunks, CancellationToken.None);

        await act.Should().NotThrowAsync();
        var added = AddedEmbeddings(repo);
        added.Select(e => e.Chunk.Content).Should().Equal("first", "second");
    }

    [Fact]
    public async Task GenerateEmbeddings_MoreEmbeddingsThanChunks_PersistsAlignedSubsetWithoutThrowing()
    {
        // Asymmetric sibling to the too-low pin above. Under the new
        // `Math.Min`-aligned contract a regression in EITHER direction is
        // observable, but the two cases catch different collapses:
        //
        //  - Too-low (above): a change to `Math.Max` or to indexing by
        //    `embeddings.Count` would over-read and throw / persist garbage.
        //  - Too-high (here): a change that iterates `embeddings.Count` instead
        //    of the min would index past the chunk list; one that iterates
        //    `chunks.Count` is correct and drops the surplus vector by design
        //    (the embedding service can legitimately emit more vectors than
        //    chunks — a hosted batching bug, a duplicate-response retry, or a
        //    misconfigured multi-vector model).
        //
        // Contract: 2 chunks, 3 embeddings → exactly the 2 chunks are
        // persisted, the surplus vector is dropped, and nothing throws.
        var documentId = Guid.NewGuid();
        var chunks = new List<Chunk>
        {
            new() { DocumentId = documentId, Content = "first" },
            new() { DocumentId = documentId, Content = "second" },
        };

        var embeddingClient = Substitute.For<IEmbeddingClient>();
        embeddingClient
            .GenerateEmbeddings(Arg.Any<List<string>>())
            .Returns(
                new List<float[]>
                {
                    new float[] { 0.1f },
                    new float[] { 0.2f },
                    new float[] { 0.3f },
                }
            );

        var (sut, repo) = CreateSutWithRepo(embeddingClient);

        var act = () => sut.GenerateEmbeddings(chunks, CancellationToken.None);

        await act.Should().NotThrowAsync();
        var added = AddedEmbeddings(repo);
        added.Select(e => e.Chunk.Content).Should().Equal("first", "second");
    }
}

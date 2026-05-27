using Equibles.Sec.BusinessLogic.Embeddings;
using Equibles.Sec.BusinessLogic.Processing;
using Equibles.Sec.BusinessLogic.Tokenization;
using Equibles.Sec.Data.Models.Chunks;
using Equibles.Sec.Repositories;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Equibles.IntegrationTests.Sec;

/// <summary>
/// Guards the systemic-failure path raised in the PR #744 review: per-chunk
/// isolation must not regress total outage into a silent, backoff-free hot
/// loop. Contract: when the embedding server is fully down — every chunk comes
/// back unembedded (null) — GenerateEmbeddings must throw, so the worker's
/// base loop logs it loudly, reports it, and backs off for a cycle instead of
/// re-pulling the same batch every iteration with zero progress and no error.
/// A few poison chunks among otherwise-successful ones must NOT throw (that is
/// covered by <see cref="DocumentProcessorPerDocumentIsolationTests"/>).
/// </summary>
public class DocumentProcessorSystemicFailureBackoffTests
{
    [Fact]
    public async Task GenerateEmbeddings_EveryChunkFailsToEmbed_ThrowsSoTheWorkerBacksOff()
    {
        var docA = Guid.NewGuid();
        var docB = Guid.NewGuid();
        var chunks = new List<Chunk>
        {
            new() { DocumentId = docA, Content = "doc-a-content" },
            new() { DocumentId = docB, Content = "doc-b-content" },
        };

        // Embedding server is down: the client stays aligned to its input but
        // every entry is null (no vector could be produced for any chunk).
        var embeddingClient = Substitute.For<IEmbeddingClient>();
        embeddingClient
            .GenerateEmbeddings(Arg.Any<List<string>>())
            .Returns(ci => new List<float[]>(new float[][] { null }));

        var embeddingRepository = Substitute.For<EmbeddingRepository>(
            (Equibles.Data.EquiblesFinancialDbContext)null
        );
        var sut = new DocumentProcessor(
            Substitute.For<ChunkRepository>((Equibles.Data.EquiblesFinancialDbContext)null),
            embeddingRepository,
            embeddingClient,
            new ChunkingStrategy(new TokenCounter()),
            Options.Create(new EmbeddingConfig { ModelName = "nomic-embed-text" }),
            Substitute.For<ILogger<DocumentProcessor>>()
        );

        // Nothing embedded across the whole batch ⇒ systemic outage ⇒ throw,
        // so BaseScraperWorker logs/reports it and waits a cycle.
        var act = async () => await sut.GenerateEmbeddings(chunks, CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>();

        // And it must not have persisted anything.
        embeddingRepository
            .ReceivedCalls()
            .Count(c => c.GetMethodInfo().Name == nameof(EmbeddingRepository.Add))
            .Should()
            .Be(0);
    }
}

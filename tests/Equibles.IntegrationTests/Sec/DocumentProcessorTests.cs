using Equibles.Sec.BusinessLogic.Embeddings;
using Equibles.Sec.BusinessLogic.Processing;
using Equibles.Sec.BusinessLogic.Tokenization;
using Equibles.Sec.Data.Models.Chunks;
using Equibles.Sec.Repositories;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Equibles.IntegrationTests.Sec;

public class DocumentProcessorTests {
    private static DocumentProcessor CreateSut(IEmbeddingClient embeddingClient) {
        return new DocumentProcessor(
            Substitute.For<ChunkRepository>((Equibles.Data.EquiblesDbContext)null),
            Substitute.For<EmbeddingRepository>((Equibles.Data.EquiblesDbContext)null),
            embeddingClient,
            new ChunkingStrategy(new TokenCounter()),
            Options.Create(new EmbeddingConfig { ModelName = "test-model" }),
            Substitute.For<ILogger<DocumentProcessor>>());
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

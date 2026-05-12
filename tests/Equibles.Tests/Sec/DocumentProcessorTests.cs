using Equibles.Sec.BusinessLogic.Embeddings;
using Equibles.Sec.BusinessLogic.Processing;
using Equibles.Sec.BusinessLogic.Tokenization;
using Equibles.Sec.Data.Models.Chunks;
using Equibles.Sec.Repositories;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Equibles.Tests.Sec;

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

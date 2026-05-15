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
/// The existing <see cref="DocumentProcessorTests"/> pins the failure modes for
/// <c>GenerateEmbeddings</c> (cancellation, count mismatch high, count mismatch low).
/// The happy path is the largest remaining branch in <c>GenerateEmbeddingsForChunks</c>:
/// the embedding-client returns one vector per chunk, the for-loop wraps each into an
/// Embedding with the configured ModelName, and SaveChanges flushes once. A regression
/// that aliased <c>chunks[i]</c> to the wrong index (very easy to write while
/// "simplifying" the loop) would persist mismatched chunk/vector pairs and corrupt
/// downstream similarity search with no exception or log signal.
/// </summary>
public class DocumentProcessorGenerateEmbeddingsTests
{
    [Fact]
    public async Task GenerateEmbeddings_ThreeChunksMatchingVectors_AddsOneEmbeddingPerChunkAndSaves()
    {
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
            .Returns(
                new List<float[]>
                {
                    new float[] { 0.1f, 0.2f },
                    new float[] { 0.3f, 0.4f },
                    new float[] { 0.5f, 0.6f },
                }
            );

        var embeddingRepository = Substitute.For<EmbeddingRepository>(
            (Equibles.Data.EquiblesDbContext)null
        );
        var sut = new DocumentProcessor(
            Substitute.For<ChunkRepository>((Equibles.Data.EquiblesDbContext)null),
            embeddingRepository,
            embeddingClient,
            new ChunkingStrategy(new TokenCounter()),
            Options.Create(new EmbeddingConfig { ModelName = "nomic-embed-text" }),
            Substitute.For<ILogger<DocumentProcessor>>()
        );

        await sut.GenerateEmbeddings(chunks, CancellationToken.None);

        var addedEmbeddings = embeddingRepository
            .ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == nameof(EmbeddingRepository.Add))
            .Select(c => (Embedding)c.GetArguments()[0])
            .ToList();

        addedEmbeddings.Should().HaveCount(3);
        // Pair each chunk with its matching vector by index — a regression that
        // mis-aligned chunks[i] vs embeddings[i] (e.g. iterated embeddings instead
        // of chunks) would scramble the pairing and fail this assertion.
        addedEmbeddings[0].Chunk.Content.Should().Be("first");
        addedEmbeddings[1].Chunk.Content.Should().Be("second");
        addedEmbeddings[2].Chunk.Content.Should().Be("third");
        addedEmbeddings
            .Should()
            .AllSatisfy(e =>
            {
                e.Model.Should().Be("nomic-embed-text");
                e.VectorDimension.Should().Be(2);
            });
        await embeddingRepository.Received(1).SaveChanges();
    }
}

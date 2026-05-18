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
/// Adversarial sibling to <see cref="DocumentProcessorGenerateEmbeddingsTests"/>.
/// Contract (PR #744 intent + the per-document try/catch in GenerateEmbeddings):
/// chunks are grouped per document and a failure embedding one document must be
/// isolated — the remaining documents must still be embedded, never aborted by
/// the first failure. One bad document must not kill the whole batch/worker.
/// </summary>
public class DocumentProcessorPerDocumentIsolationTests
{
    [Fact]
    public async Task GenerateEmbeddings_FirstDocumentFails_StillEmbedsTheSecondDocument()
    {
        var docA = Guid.NewGuid();
        var docB = Guid.NewGuid();
        var chunks = new List<Chunk>
        {
            new() { DocumentId = docA, Content = "doc-a-content" },
            new() { DocumentId = docB, Content = "doc-b-content" },
        };

        var embeddingClient = Substitute.For<IEmbeddingClient>();
        embeddingClient
            .GenerateEmbeddings(Arg.Is<List<string>>(l => l.Contains("doc-a-content")))
            .Returns(Task.FromException<List<float[]>>(new HttpRequestException("server down")));
        embeddingClient
            .GenerateEmbeddings(Arg.Is<List<string>>(l => l.Contains("doc-b-content")))
            .Returns(new List<float[]> { new float[] { 0.1f, 0.2f } });

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

        // doc A's failure must be isolated; the call must complete and doc B
        // must still be embedded — not aborted by the first document's error.
        var act = async () => await sut.GenerateEmbeddings(chunks, CancellationToken.None);
        await act.Should().NotThrowAsync();

        var added = embeddingRepository
            .ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == nameof(EmbeddingRepository.Add))
            .Select(c => (Embedding)c.GetArguments()[0])
            .ToList();
        added.Should().ContainSingle(e => e.Chunk.Content == "doc-b-content");
    }
}

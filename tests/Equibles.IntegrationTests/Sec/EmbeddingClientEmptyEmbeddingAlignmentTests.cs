using System.Net;
using System.Text;
using Equibles.Sec.BusinessLogic.Embeddings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Equibles.IntegrationTests.Sec;

/// <summary>
/// Adversarial sibling to <see cref="EmbeddingClientMultiTextBatchTests"/>.
/// Contract (from the only caller, DocumentProcessor.GenerateEmbeddingsForChunks):
/// it guards with <c>if (embeddings.Count != chunks.Count) throw</c> and then
/// indexes <c>embeddings[i]</c> against <c>chunks[i]</c> — so GenerateEmbeddings
/// must return one entry per input text, positionally aligned. A text whose
/// vector cannot be produced must keep its slot; silently dropping it shifts
/// every later vector onto the wrong chunk and trips the count guard, killing
/// embedding generation for the whole document.
/// </summary>
public class EmbeddingClientEmptyEmbeddingAlignmentTests
{
    [Fact]
    public async Task GenerateEmbeddings_OneTextReturnsEmptyEmbedding_StaysPositionallyAlignedToInputs()
    {
        // Ollama answers 200 but with no vector for the 2nd input (degenerate
        // model output). Per the caller's contract the result must still have
        // one entry per input, in order — never silently shrink/misalign.
        var handler = new SequencedHandler(
            "{\"model\":\"nomic-embed-text\",\"embeddings\":[[0.1,0.2]]}",
            "{\"model\":\"nomic-embed-text\",\"embeddings\":[]}"
        );
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>()).Returns(new HttpClient(handler));

        var sut = new EmbeddingClient(
            factory,
            Options.Create(
                new EmbeddingConfig
                {
                    Enabled = true,
                    BaseUrl = "http://ollama.test",
                    ModelName = "nomic-embed-text",
                    BatchSize = 10,
                }
            ),
            Substitute.For<ILogger<EmbeddingClient>>()
        );

        var vectors = await sut.GenerateEmbeddings(["first chunk", "second chunk"]);

        vectors.Should().HaveCount(2);
        vectors[0].Should().Equal(0.1f, 0.2f);
    }

    private sealed class SequencedHandler : HttpMessageHandler
    {
        private readonly Queue<string> _bodies;

        public SequencedHandler(params string[] bodies) => _bodies = new Queue<string>(bodies);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            var body = _bodies.Dequeue();
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json"),
                }
            );
        }
    }
}

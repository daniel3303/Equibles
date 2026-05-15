using System.Net;
using System.Text;
using Equibles.Sec.BusinessLogic.Embeddings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Equibles.IntegrationTests.Sec;

/// <summary>
/// Sibling to <see cref="EmbeddingClientGenerateTests"/>, which pins the
/// <c>batch.Count == 1</c> shortcut. This pins the multi-text branch of
/// ProcessBatch (lines 113-137, zero-hit): Ollama's /api/embed takes one input
/// at a time, so a batch of N must POST N times and collect one vector each. A
/// regression that broke the per-text loop (e.g. posted the whole batch once or
/// dropped <c>Embeddings[0]</c>) would silently shrink every multi-chunk
/// document's embedding set with no exception.
/// </summary>
public class EmbeddingClientMultiTextBatchTests
{
    [Fact]
    public async Task GenerateEmbeddings_MultipleTextsInOneBatch_PostsPerTextAndReturnsVectorEach()
    {
        var responseBody = "{\"model\":\"nomic-embed-text\",\"embeddings\":[[0.5,0.6]]}";
        var handler = new CountingHandler(responseBody);
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

        // Two inputs in one batch → two separate /api/embed POSTs → two vectors.
        handler.CallCount.Should().Be(2);
        vectors.Should().HaveCount(2);
        vectors[0].Should().Equal(0.5f, 0.6f);
        vectors[1].Should().Equal(0.5f, 0.6f);
    }

    private sealed class CountingHandler : HttpMessageHandler
    {
        private readonly string _body;
        public int CallCount { get; private set; }

        public CountingHandler(string body) => _body = body;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            CallCount++;
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(_body, Encoding.UTF8, "application/json"),
                }
            );
        }
    }
}

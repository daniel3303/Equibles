using System.Net;
using System.Text;
using Equibles.Sec.BusinessLogic.Embeddings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Equibles.IntegrationTests.Sec;

/// <summary>
/// Sibling to <see cref="EmbeddingClientGenerateTests"/>, which pins the
/// single-text shortcut. This pins the multi-text branch of ProcessBatch:
/// Ollama's /api/embed takes one input at a time, so a batch of N must POST N
/// times (now concurrently) and collect one vector each, positionally aligned to
/// the inputs. A regression that posted the whole batch once, dropped
/// <c>Embeddings[0]</c>, or reordered the results would silently corrupt every
/// multi-chunk document's embedding set with no exception.
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
        private int _callCount;

        // The batch is now embedded concurrently, so SendAsync can be entered from several
        // threads at once — count atomically.
        public int CallCount => Volatile.Read(ref _callCount);

        public CountingHandler(string body) => _body = body;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Interlocked.Increment(ref _callCount);
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(_body, Encoding.UTF8, "application/json"),
                }
            );
        }
    }
}

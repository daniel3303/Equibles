using System.Net;
using System.Text;
using Equibles.Sec.BusinessLogic.Embeddings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Equibles.IntegrationTests.Sec;

/// <summary>
/// Sibling to <see cref="EmbeddingClientTests"/>. That file pins the disabled-config
/// early-return paths and the bearer-header constructor wiring; the actual HTTP
/// happy path through <c>ProcessBatch</c> is uncovered. Pins the per-provider request:
/// the default <see cref="EmbeddingProvider.Ollama"/> path posts to <c>/api/embed</c> and
/// reads the <c>"embeddings"</c> field, while <see cref="EmbeddingProvider.OpenAI"/> posts to
/// <c>/v1/embeddings</c> and reads <c>data[0].embedding</c> — getting the endpoint or the
/// response field wrong for a provider silently returns empty vectors with no exception or log.
/// </summary>
public class EmbeddingClientGenerateTests
{
    [Fact]
    public async Task GenerateEmbedding_SingleText_PostsToApiEmbedAndReturnsVector()
    {
        var responseBody = "{\"model\":\"nomic-embed-text\",\"embeddings\":[[0.1,0.2,0.3,0.4]]}";
        var handler = new CapturingHandler(responseBody);
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
                }
            ),
            Substitute.For<ILogger<EmbeddingClient>>()
        );

        var vector = await sut.GenerateEmbedding("Apple Inc. financial filing");

        vector.Should().NotBeNull();
        vector.Should().Equal(0.1f, 0.2f, 0.3f, 0.4f);

        // POST must hit /api/embed — a regression that swapped the path to /v1/embeddings
        // (OpenAI-style) would still return a 200 from a generic mock but would not exist
        // on a real Ollama server.
        handler.LastUrl.Should().EndWith("/api/embed");
        handler.LastMethod.Should().Be(HttpMethod.Post);
        handler.LastBody.Should().Contain("\"model\":\"nomic-embed-text\"");
        handler.LastBody.Should().Contain("\"input\":\"Apple Inc. financial filing\"");
    }

    [Fact]
    public async Task GenerateEmbedding_OpenAiProvider_PostsToV1EmbeddingsAndReturnsVector()
    {
        // OpenAI-compatible servers (vLLM, TEI) return { "data": [ { "embedding": [..] } ] }.
        var responseBody =
            "{\"object\":\"list\",\"data\":[{\"object\":\"embedding\",\"index\":0,\"embedding\":[0.5,0.6,0.7]}],\"model\":\"qwen3-embedding:0.6b\"}";
        var handler = new CapturingHandler(responseBody);
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>()).Returns(new HttpClient(handler));

        var sut = new EmbeddingClient(
            factory,
            Options.Create(
                new EmbeddingConfig
                {
                    Enabled = true,
                    Provider = EmbeddingProvider.OpenAI,
                    BaseUrl = "http://vllm.test",
                    ModelName = "qwen3-embedding:0.6b",
                }
            ),
            Substitute.For<ILogger<EmbeddingClient>>()
        );

        var vector = await sut.GenerateEmbedding("Apple Inc. financial filing");

        vector.Should().NotBeNull();
        vector.Should().Equal(0.5f, 0.6f, 0.7f);

        // OpenAI provider must hit /v1/embeddings, send input as an ARRAY (so vLLM batches it),
        // and read data[].embedding — not /api/embed.
        handler.LastUrl.Should().EndWith("/v1/embeddings");
        handler.LastMethod.Should().Be(HttpMethod.Post);
        handler.LastBody.Should().Contain("\"model\":\"qwen3-embedding:0.6b\"");
        handler.LastBody.Should().Contain("\"input\":[\"Apple Inc. financial filing\"]");
    }

    [Fact]
    public async Task GenerateEmbeddings_OpenAiProvider_SendsOneArrayRequestAndAlignsByIndex()
    {
        // The whole batch goes in ONE request as an array; the server returns one entry per input
        // with an `index`. Return them out of order to prove we align by index, not array order.
        var responseBody =
            "{\"object\":\"list\",\"data\":["
            + "{\"index\":1,\"embedding\":[1.0,1.1]},"
            + "{\"index\":0,\"embedding\":[0.0,0.1]}"
            + "],\"model\":\"qwen3-embedding:0.6b\"}";
        var handler = new CapturingHandler(responseBody);
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>()).Returns(new HttpClient(handler));

        var sut = new EmbeddingClient(
            factory,
            Options.Create(
                new EmbeddingConfig
                {
                    Enabled = true,
                    Provider = EmbeddingProvider.OpenAI,
                    BaseUrl = "http://vllm.test",
                    ModelName = "qwen3-embedding:0.6b",
                    BatchSize = 16,
                }
            ),
            Substitute.For<ILogger<EmbeddingClient>>()
        );

        var vectors = await sut.GenerateEmbeddings(["first chunk", "second chunk"]);

        // Two inputs, a SINGLE request (not one per text), results aligned to input order by index.
        handler.RequestCount.Should().Be(1);
        handler.LastBody.Should().Contain("\"input\":[\"first chunk\",\"second chunk\"]");
        vectors.Should().HaveCount(2);
        vectors[0].Should().Equal(0.0f, 0.1f);
        vectors[1].Should().Equal(1.0f, 1.1f);
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        private readonly string _body;
        public string LastUrl { get; private set; }
        public HttpMethod LastMethod { get; private set; }
        public string LastBody { get; private set; }
        public int RequestCount { get; private set; }

        public CapturingHandler(string body) => _body = body;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            RequestCount++;
            LastUrl = request.RequestUri!.AbsoluteUri;
            LastMethod = request.Method;
            LastBody =
                request.Content != null
                    ? await request.Content.ReadAsStringAsync(cancellationToken)
                    : "";
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json"),
            };
        }
    }
}

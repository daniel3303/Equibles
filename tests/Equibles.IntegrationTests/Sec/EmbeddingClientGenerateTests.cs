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
/// happy path through <c>ProcessBatch</c> is uncovered. Pins the single-text
/// shortcut (the <c>batch.Count == 1</c> branch) and the <c>/api/embed</c>
/// request URL — a refactor that swapped the endpoint to OpenAI's <c>/v1/embeddings</c>
/// or dropped the JSON binding to the typo-prone <c>"embeddings"</c> field would
/// silently return empty vectors with no exception or log.
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

    private sealed class CapturingHandler : HttpMessageHandler
    {
        private readonly string _body;
        public string LastUrl { get; private set; }
        public HttpMethod LastMethod { get; private set; }
        public string LastBody { get; private set; }

        public CapturingHandler(string body) => _body = body;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            LastUrl = request.RequestUri!.AbsoluteUri;
            LastMethod = request.Method;
            LastBody = request.Content != null
                ? await request.Content.ReadAsStringAsync(cancellationToken)
                : "";
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json"),
            };
        }
    }
}

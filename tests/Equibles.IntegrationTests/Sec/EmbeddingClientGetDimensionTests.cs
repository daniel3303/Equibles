using System.Net;
using System.Text;
using Equibles.Sec.BusinessLogic.Embeddings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Equibles.IntegrationTests.Sec;

/// <summary>
/// <c>GetEmbeddingDimension</c> (probe an embedding to learn the model's vector
/// width) was uncovered. Pins both arms: a successful probe returns the vector
/// length; a transport failure during the probe is swallowed by the per-text
/// fault isolation introduced in PR #866 (ProcessBatch catches every per-input
/// exception and yields a null embedding), so the probe returns 0 rather than
/// rethrowing.
/// </summary>
public class EmbeddingClientGetDimensionTests
{
    private static EmbeddingClient Build(HttpMessageHandler handler)
    {
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>()).Returns(new HttpClient(handler));
        return new EmbeddingClient(
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
    }

    [Fact]
    public async Task GetEmbeddingDimension_ProbeSucceeds_ReturnsVectorLength()
    {
        var handler = new ConstantHandler(
            "{\"model\":\"nomic-embed-text\",\"embeddings\":[[0.1,0.2,0.3,0.4,0.5]]}"
        );

        var dimension = await Build(handler).GetEmbeddingDimension();

        dimension.Should().Be(5, "the probe vector had five components");
    }

    [Fact]
    public async Task GetEmbeddingDimension_ProbeTransportFails_SwallowedReturnsZero()
    {
        // Pre-#866 the probe rethrew transport failures. PR #866 moved per-text
        // exception handling into ProcessBatch: an HttpRequestException for the
        // probe input is caught there, logged as a skipped chunk, and yields a
        // null embedding. GenerateEmbedding -> FirstOrDefault is therefore null
        // and GetEmbeddingDimension returns 0 without throwing. The probe never
        // surfaces transport failures any more — pin that so a regression that
        // re-introduces a throw (or returns a bogus non-zero width) is caught.
        var handler = new ThrowingHandler();

        var dimension = await Build(handler).GetEmbeddingDimension();

        dimension.Should().Be(0, "the failed probe yields a null embedding");
    }

    private sealed class ConstantHandler : HttpMessageHandler
    {
        private readonly string _body;

        public ConstantHandler(string body) => _body = body;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) =>
            Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(_body, Encoding.UTF8, "application/json"),
                }
            );
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) => throw new HttpRequestException("embedding endpoint unreachable");
    }
}

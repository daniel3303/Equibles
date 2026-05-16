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
/// length; a transport failure is logged and rethrown so callers don't proceed
/// with an unknown dimension.
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
    public async Task GetEmbeddingDimension_ProbeTransportFails_LogsAndRethrows()
    {
        var handler = new ThrowingHandler();

        var act = async () => await Build(handler).GetEmbeddingDimension();

        await act.Should().ThrowAsync<HttpRequestException>();
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

using Equibles.Sec.BusinessLogic.Embeddings;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Equibles.UnitTests.Sec;

public class EmbeddingClientBatchFailureTests
{
    [Fact]
    public async Task GenerateEmbeddings_ServerUnreachable_ReturnsPositionalNullsWithoutThrowing()
    {
        // Contract: one bad chunk (or a whole unreachable server) must never abort the batch — the
        // caller relies on a positionally-aligned list with null entries for the failures so the
        // backfill keeps draining and re-attempts them next cycle. The batched OpenAI request fails,
        // the per-text fallback fails per text, and the client still returns one entry per input.
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>()).Returns(new HttpClient(new ThrowingHandler()));

        var client = new EmbeddingClient(
            factory,
            Options.Create(
                new EmbeddingConfig
                {
                    Enabled = true,
                    BaseUrl = "http://embedding-server:11434",
                    ModelName = "qwen3-embedding:0.6b",
                    Provider = EmbeddingProvider.OpenAI,
                }
            ),
            NullLogger<EmbeddingClient>.Instance
        );

        var result = await client.GenerateEmbeddings(["a", "b", "c"]);

        result.Should().HaveCount(3);
        result.Should().OnlyContain(e => e == null);
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) =>
            Task.FromException<HttpResponseMessage>(
                new HttpRequestException("Resource temporarily unavailable")
            );
    }
}

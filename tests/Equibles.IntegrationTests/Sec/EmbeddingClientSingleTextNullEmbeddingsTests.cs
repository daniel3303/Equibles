using System.Net;
using System.Text;
using Equibles.Sec.BusinessLogic.Embeddings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Equibles.IntegrationTests.Sec;

/// <summary>
/// Adversarial sibling to <see cref="EmbeddingClientGenerateTests"/>, which pins
/// the single-text happy path. Contract (from GenerateEmbedding's
/// <c>FirstOrDefault()</c> and the caller DocumentProcessor, which indexes the
/// result per input): GenerateEmbeddings returns a list aligned to its inputs
/// and a single degenerate Ollama 200 (no <c>embeddings</c> field) must not
/// tear the call down — otherwise one bad chunk kills the whole worker.
/// </summary>
public class EmbeddingClientSingleTextNullEmbeddingsTests
{
    [Fact(
        Skip = "GH-825 — EmbeddingClient.GenerateEmbeddings single-text branch throws "
            + "ArgumentNullException when Ollama 200 has no embeddings field"
    )]
    public async Task GenerateEmbeddings_SingleTextOllamaReturnsNoEmbeddingsField_DoesNotThrowAndStaysAligned()
    {
        // Ollama answers 200 but omits the "embeddings" field for this input
        // (degenerate model output) → OllamaEmbedResponse.Embeddings is null.
        // Per contract this must yield one entry per input, never tear down.
        var handler = new StubHandler("{\"model\":\"nomic-embed-text\"}");
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

        var act = async () => await sut.GenerateEmbeddings(["only text"]);

        var vectors = (await act.Should().NotThrowAsync()).Subject;
        vectors.Should().HaveCount(1);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly string _body;

        public StubHandler(string body) => _body = body;

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
}

using Equibles.Sec.BusinessLogic.Embeddings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Equibles.IntegrationTests.Sec;

public class EmbeddingClientTests {
    [Fact]
    public async Task GenerateEmbeddings_DisabledConfig_ReturnsEmptyWithoutHttpCall() {
        var httpFactory = Substitute.For<IHttpClientFactory>();
        httpFactory.CreateClient(Arg.Any<string>()).Returns(new HttpClient());
        var config = Options.Create(new EmbeddingConfig { Enabled = false });
        var sut = new EmbeddingClient(httpFactory, config, Substitute.For<ILogger<EmbeddingClient>>());

        var result = await sut.GenerateEmbeddings(new List<string> { "any text" });

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GenerateEmbeddings_EmptyTextList_ShortCircuitsBeforeConfigCheck() {
        // The companion DisabledConfig test exercises the `!_config.IsConfigured`
        // half of the early-return OR. This sibling exercises the OTHER half —
        // `!texts.Any()` — so the regression "both halves were collapsed to the
        // config check only" is caught. An empty texts list with a fully
        // configured client must still short-circuit to an empty result and
        // skip the Ollama HTTP call (which on a working setup would otherwise
        // POST an empty payload and waste a request). Pin the empty-texts
        // path so the regression surfaces here.
        var httpFactory = Substitute.For<IHttpClientFactory>();
        httpFactory.CreateClient(Arg.Any<string>()).Returns(new HttpClient());
        var config = Options.Create(new EmbeddingConfig {
            Enabled = true,
            BaseUrl = "http://localhost:11434",
            ModelName = "nomic-embed-text",
        });
        var sut = new EmbeddingClient(httpFactory, config, Substitute.For<ILogger<EmbeddingClient>>());

        var result = await sut.GenerateEmbeddings([]);

        result.Should().BeEmpty();
    }
}

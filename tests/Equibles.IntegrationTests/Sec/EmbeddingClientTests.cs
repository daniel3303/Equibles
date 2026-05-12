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
}

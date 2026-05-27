using Equibles.Sec.BusinessLogic.Embeddings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Equibles.IntegrationTests.Sec;

/// <summary>
/// Companion to EmbeddingClientTests (which pins GenerateEmbeddings's two early-
/// return arms). `IsEnabled` is the public-property gate that callers (and the
/// status badge in the web UI) read to decide whether the embedding pipeline
/// is wired up — it forwards to `EmbeddingConfig.IsConfigured`, which requires
/// `Enabled` AND non-empty `BaseUrl` AND non-empty `ModelName`. A refactor that
/// simplified `IsEnabled` to `_config.Enabled` alone (the most common
/// shortcut) would silently flip the badge to "configured" for a half-set-up
/// deployment (Enabled but no BaseUrl), so downstream code would then attempt
/// HTTP requests against a null/empty base URL and crash on the first chunk.
/// Pin the partial-config-is-not-configured contract.
/// </summary>
public class EmbeddingClientIsEnabledPartialConfigTests
{
    [Fact]
    public void IsEnabled_EnabledTrueButBaseUrlMissing_ReturnsFalse()
    {
        var httpFactory = Substitute.For<IHttpClientFactory>();
        httpFactory.CreateClient(Arg.Any<string>()).Returns(new HttpClient());
        var config = Options.Create(
            new EmbeddingConfig
            {
                Enabled = true,
                BaseUrl = "",
                ModelName = "nomic-embed-text",
            }
        );

        var sut = new EmbeddingClient(
            httpFactory,
            config,
            Substitute.For<ILogger<EmbeddingClient>>()
        );

        sut.IsEnabled.Should().BeFalse();
    }
}

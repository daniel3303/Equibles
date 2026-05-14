using Equibles.Sec.BusinessLogic.Embeddings;

namespace Equibles.UnitTests.Sec;

public class EmbeddingConfigTests
{
    [Fact]
    public void IsConfigured_EnabledTrueButBaseUrlMissing_ReturnsFalse()
    {
        // EmbeddingConfig.IsConfigured is a three-conjunct gate
        // (`Enabled && !IsNullOrEmpty(BaseUrl) && !IsNullOrEmpty(ModelName)`) that the
        // entire RAG pipeline depends on. It's consulted from three places:
        //   1. EmbeddingClient.IsEnabled — exposes the flag to callers
        //   2. EmbeddingClient ctor — gates HTTP wiring (BaseAddress, Authorization header)
        //   3. EmbeddingClient.GenerateEmbeddings — short-circuits BEFORE any HTTP call
        // The constructor's gate is the dangerous one: `_httpClient.BaseAddress = new Uri(_config.BaseUrl)`
        // throws ArgumentNullException if BaseUrl is null/empty. A regression that
        // dropped the `!IsNullOrEmpty(BaseUrl)` conjunct (e.g. someone "simplifies"
        // IsConfigured to just `Enabled` thinking the other two are always set) would
        // cause every scoped EmbeddingClient creation to crash on the first request
        // for any partial-config deployment — Enabled=true is the typical staging
        // tweak, but operators routinely forget to set the URL in a fresh environment.
        //
        // The risk this test pins: an existing sibling (EmbeddingClientTests in the
        // integration tier) covers the "Enabled=false" path transitively, but does not
        // distinguish "Enabled=false alone is enough" from "all three conjuncts matter".
        // This direct test on EmbeddingConfig.IsConfigured pins the BaseUrl conjunct
        // explicitly — Enabled=true and ModelName is set (so the failure can only come
        // from the missing BaseUrl), and IsConfigured must still return false.
        //
        // Pick null specifically (not just empty string) so the test also exercises
        // the IsNullOrEmpty null path — the worst silent failure mode, since null
        // BaseUrl is what `new Uri(_config.BaseUrl)` would actually trip on.
        var config = new EmbeddingConfig
        {
            Enabled = true,
            BaseUrl = null,
            ModelName = "nomic-embed-text",
        };

        config.IsConfigured.Should().BeFalse();
    }
}

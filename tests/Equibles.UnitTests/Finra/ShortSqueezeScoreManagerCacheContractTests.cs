using Equibles.Finra.BusinessLogic;

namespace Equibles.UnitTests.Finra;

public class ShortSqueezeScoreManagerCacheContractTests
{
    // The key is a cross-process contract, not an implementation detail: the MCP
    // tool caches Compute() under it, and downstream deployments (the commercial
    // repo's ShortSqueezeScoreProvider and its background warmer) reference the
    // same constant so ONE entry per process serves every surface. Renaming the
    // literal would silently split that entry into per-surface copies — each
    // paying its own whole-universe build — with nothing else failing.
    [Fact]
    public void UniverseCacheKey_IsThePinnedCrossRepoLiteral()
    {
        ShortSqueezeScoreManager.UniverseCacheKey.Should().Be("short-squeeze-scores");
    }

    [Fact]
    public void UniverseCacheDuration_StaysAtOneHour()
    {
        // Consumers size their background-refresh intervals under this lifetime;
        // shortening it below a deployed warm interval would let entries expire
        // between refreshes and reintroduce the cold-call cost.
        ShortSqueezeScoreManager.UniverseCacheDuration.Should().Be(TimeSpan.FromHours(1));
    }
}

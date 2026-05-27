using System.Reflection;
using Equibles.Integrations.Sec;

namespace Equibles.UnitTests.Sec;

public class SecEdgarClientTransientBackoffCapTests
{
    // SecEdgarClient.TransientBackoff doubles per attempt — `2^(attempt+1)`
    // seconds — which crosses the 5-minute MaxRetryDelay at attempt=8
    // (2^9 = 512s). The closing `backoff > MaxRetryDelay ? MaxRetryDelay
    // : backoff` cap is the load-bearing safety: it prevents a single
    // wedged scraper retry from blocking the entire SEC-scraping cycle for
    // 17+ minutes when DNS or TLS keeps failing on a recovering network.
    // The Retry-After-driven sibling (GetRetryDelay) has its own cap pin;
    // this is the transient-network sibling. A refactor that dropped the
    // cap would compile cleanly and silently extend the worst-case retry
    // window past the SEC scraper's overall budget.
    [Fact]
    public void TransientBackoff_AttemptCountExceedingCapThreshold_CapsAtFiveMinutes()
    {
        var method = typeof(SecEdgarClient).GetMethod(
            "TransientBackoff",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var result = (TimeSpan)method.Invoke(null, [10]);

        result.Should().Be(TimeSpan.FromMinutes(5));
    }
}

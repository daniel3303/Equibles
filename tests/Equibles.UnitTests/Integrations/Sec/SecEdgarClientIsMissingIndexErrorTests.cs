using System.Reflection;
using Equibles.Integrations.Sec;

namespace Equibles.UnitTests.Integrations.Sec;

public class SecEdgarClientIsMissingIndexErrorTests
{
    // Contract: IsMissingIndexError returns true ONLY when the 403 body positively
    // identifies a missing S3 object (AccessDenied / NoSuchKey) — the non-trading-day
    // signature that authorizes SKIPPING the day. SEC's rate-limit 403 ("Request Rate
    // Threshold Exceeded") is a DIFFERENT 403; classifying it as missing-index would
    // silently drop a throttled trading day's filings. Pin that it is not.
    [Fact]
    public void IsMissingIndexError_RateLimitThresholdPage_ReturnsFalse()
    {
        const string throttleBody =
            "<html><head><title>Request Rate Threshold Exceeded</title></head>"
            + "<body>You have exceeded the SEC's request rate limit.</body></html>";

        var method = typeof(SecEdgarClient).GetMethod(
            "IsMissingIndexError",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var result = (bool)method!.Invoke(null, [throttleBody]);

        result.Should().BeFalse();
    }
}

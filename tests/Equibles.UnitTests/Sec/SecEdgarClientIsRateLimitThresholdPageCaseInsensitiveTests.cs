using System.Reflection;
using Equibles.Integrations.Sec;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// Adversarial sibling to <see cref="SecEdgarClientIsRateLimitThresholdPageTests"/>,
/// which only feeds the phrase in SEC's current title casing. The detector declares
/// StringComparison.OrdinalIgnoreCase, so a casing change from SEC (or a refactor to
/// Ordinal) must not stop it recognising the throttle page — otherwise a 403 throttle
/// is mistaken for a non-trading day and the sweep watermark advances past throttled
/// data, silently dropping daily-index days (GH-2222).
/// </summary>
public class SecEdgarClientIsRateLimitThresholdPageCaseInsensitiveTests
{
    private static bool Invoke(string body)
    {
        var method = typeof(SecEdgarClient).GetMethod(
            "IsRateLimitThresholdPage",
            BindingFlags.NonPublic | BindingFlags.Static
        );
        return (bool)method.Invoke(null, [body]);
    }

    [Fact]
    public void IsRateLimitThresholdPage_PhraseInLowercase_ReturnsTrue()
    {
        var body =
            "<html><head><title>sec.gov | request rate threshold exceeded</title></head></html>";

        Invoke(body).Should().BeTrue();
    }
}

using System.Reflection;
using Equibles.Integrations.Sec;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// Pins IsRateLimitThresholdPage — the discriminator that lets SendWithRetryAsync
/// tell SEC's 403 throttle page ("Request Rate Threshold Exceeded") apart from a
/// genuine Forbidden. Broaden it and real 403s would be retried forever; narrow
/// it and the worker resumes silently dropping throttled daily-index days
/// (GH-2222).
/// </summary>
public class SecEdgarClientIsRateLimitThresholdPageTests
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
    public void IsRateLimitThresholdPage_ThrottlePageBody_ReturnsTrue()
    {
        var body =
            "<html><head><title>SEC.gov | Request Rate Threshold Exceeded</title></head></html>";

        Invoke(body).Should().BeTrue();
    }

    [Fact]
    public void IsRateLimitThresholdPage_NormalMasterIndexBody_ReturnsFalse()
    {
        var body =
            "CIK|Company Name|Form Type|Date Filed|File Name\n"
            + "933478|VANGUARD FIDUCIARY TRUST CO|13F-HR|20260508|edgar/data/933478/0000933478-26-000004.txt";

        Invoke(body).Should().BeFalse();
    }
}

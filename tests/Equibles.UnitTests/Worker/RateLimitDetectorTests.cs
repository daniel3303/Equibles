using Equibles.Worker;

namespace Equibles.UnitTests.Worker;

public class RateLimitDetectorTests
{
    [Fact]
    public void IsRateLimited_429Status_True()
    {
        RateLimitDetector.IsRateLimited(429, null).Should().BeTrue();
    }

    [Theory]
    [InlineData("<html><title>Error 1015</title></html>")]
    [InlineData("<body>You are being rate limited</body>")]
    [InlineData("...YOU ARE BEING RATE LIMITED...")] // case-insensitive
    public void IsRateLimited_CloudflareInterstitial_True(string html)
    {
        RateLimitDetector.IsRateLimited(null, html).Should().BeTrue();
    }

    [Theory]
    [InlineData(200, "<html><body>Investor relations</body></html>")]
    [InlineData(null, "")]
    [InlineData(404, "<html>Page not found</html>")]
    public void IsRateLimited_BenignResponse_False(int? status, string html)
    {
        RateLimitDetector.IsRateLimited(status, html).Should().BeFalse();
    }
}

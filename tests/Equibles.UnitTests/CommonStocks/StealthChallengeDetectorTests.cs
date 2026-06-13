using Equibles.CommonStocks.HostedService.Services;

namespace Equibles.UnitTests.CommonStocks;

public class StealthChallengeDetectorTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void IsChallenge_NoBody_ReturnsFalse(string body)
    {
        // Nothing to inspect is not a challenge — the caller already treats a missing
        // body as a plain miss.
        StealthChallengeDetector.IsChallenge(body).Should().BeFalse();
    }

    [Theory]
    [InlineData(
        "<html><head><script src=\"/_Incapsula_Resource?SWJIYLWA=719d\"></script></head><body></body></html>"
    )]
    [InlineData("<html><body>Request unsuccessful. Incapsula incident ID: 1234-5678</body></html>")]
    [InlineData(
        "<html><head><script src=\"https://errors.edgesuite.net/x.js\"></script></head></html>"
    )]
    [InlineData("<html><head><title>Access Denied</title></head><body></body></html>")]
    [InlineData("<html><body>You don't have permission to access this resource.</body></html>")]
    [InlineData("<html><body>Access denied, Code 1011</body></html>")]
    [InlineData(
        "<html><body><div>Request unsuccessful.</div><footer>Powered by Incapsula</footer></body></html>"
    )]
    [InlineData(
        "<html><body><h1>Pardon the interruption</h1>"
            + "<p>As you were browsing, something about your browser made us think you were a bot.</p></body></html>"
    )]
    public void IsChallenge_VendorChallengeMarker_ReturnsTrue(string body)
    {
        // A bot-protection stub carries a vendor marker and none of the real page, so
        // the plain probe would record a false miss; detecting it routes the fetch
        // through the stealth path instead.
        StealthChallengeDetector.IsChallenge(body).Should().BeTrue();
    }

    [Fact]
    public void IsChallenge_MarkerMatchIsCaseInsensitive()
    {
        StealthChallengeDetector
            .IsChallenge("<SCRIPT SRC=\"/_INCAPSULA_RESOURCE?A=1\"></SCRIPT>")
            .Should()
            .BeTrue();
    }

    [Theory]
    [InlineData(
        "<html><head><title>Investor Relations - Acme</title></head>"
            + "<body><h1>Quarterly results</h1><p>Press releases and SEC filings.</p></body></html>"
    )]
    [InlineData(
        "<html><head><title>Home</title></head><body>"
            + "<img src=\"https://cdn.akamai.com/logo.png\">Welcome</body></html>"
    )]
    public void IsChallenge_RealPage_ReturnsFalse(string body)
    {
        // A genuine page — including one that merely loads assets from a CDN such as
        // Akamai — must not be mistaken for a challenge, or every miss would be routed
        // through the expensive stealth path.
        StealthChallengeDetector.IsChallenge(body).Should().BeFalse();
    }
}

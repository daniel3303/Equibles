using Equibles.CommonStocks.HostedService.Services;

namespace Equibles.UnitTests.CommonStocks;

/// <summary>
/// Contract: <c>WebsiteProbeClient.Normalize</c> turns a source candidate into
/// the absolute http(s) URL that would be probed and persisted — https assumed
/// when the scheme is omitted, non-web schemes and host-less values rejected,
/// and anything beyond the <c>CommonStock.Website</c> column ceiling rejected.
/// </summary>
public class WebsiteProbeClientNormalizeTests
{
    [Theory]
    [InlineData("www.acme.com", "https://www.acme.com")]
    [InlineData("acme.com", "https://acme.com")]
    [InlineData("  www.acme.com  ", "https://www.acme.com")]
    [InlineData("http://acme.com", "http://acme.com")]
    [InlineData("https://www.acme.com/about", "https://www.acme.com/about")]
    public void WebUrls_NormalizeToAbsoluteHttp(string candidate, string expected)
    {
        WebsiteProbeClient.Normalize(candidate).Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ftp://acme.com")]
    [InlineData("mailto:ir@acme.com")]
    [InlineData("localhost")]
    [InlineData("not a url")]
    public void NonWebCandidates_AreRejected(string candidate)
    {
        WebsiteProbeClient.Normalize(candidate).Should().BeNull();
    }

    [Fact]
    public void CandidateBeyondColumnCeiling_IsRejected()
    {
        var candidate = "www.acme.com/" + new string('a', 256);

        WebsiteProbeClient.Normalize(candidate).Should().BeNull();
    }
}

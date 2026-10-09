using Equibles.CommonStocks.BusinessLogic.Websites;

namespace Equibles.UnitTests.CommonStocks;

/// <summary>
/// Contract: <c>IssuerWebsitePolicy.IsSharedPlatform</c> names the hosts every issuer would share
/// (social networks, marketplaces, app stores, regulators); a company's own domain, including one that
/// merely contains a platform name, is never shared.
/// </summary>
public class IssuerWebsitePolicyTests
{
    [Theory]
    [InlineData("https://www.linkedin.com")]
    [InlineData("linkedin.com/company/acme")]
    [InlineData("https://investors.linkedin.com")]
    [InlineData("https://linkedin.com./company/acme")]
    [InlineData("https://www.youtube.com/@acme")]
    [InlineData("https://en.wikipedia.org/wiki/Acme")]
    [InlineData("https://www.sec.gov/edgar")]
    public void SharedPlatformHosts_AreShared(string url)
    {
        IssuerWebsitePolicy.IsSharedPlatform(url).Should().BeTrue();
    }

    [Theory]
    [InlineData("https://www.acme.com")]
    [InlineData("acme.com")]
    [InlineData("https://ir.aboutacme.com")]
    [InlineData("https://www.amazonlogistics-partner.com")]
    [InlineData("https://linkedin-analytics.io")]
    [InlineData("https://www.applebank.com")]
    [InlineData("https://www.apple.com")]
    [InlineData("https://amazon.com")]
    [InlineData("https://m.facebook.com")]
    [InlineData("https://www.redditinc.com")]
    [InlineData("https://xcompany.com")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a url")]
    public void OwnDomains_AreNotShared(string url)
    {
        IssuerWebsitePolicy.IsSharedPlatform(url).Should().BeFalse();
    }
}

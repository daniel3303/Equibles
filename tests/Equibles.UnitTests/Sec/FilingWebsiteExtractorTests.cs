using Equibles.Sec.BusinessLogic.Websites;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// Contract: <c>FilingWebsiteExtractor.Extract</c> returns the company's own
/// disclosed website host from filing text (the Reg S-K Item 101(e) disclosure),
/// preferring the corporate host over an investor-relations one, and never
/// returns a third party's URL (the SEC's site, an email domain, a URL with no
/// self-reference context). Snippets below are modelled on real 10-K / 10-Q
/// phrasings (Apple, NVIDIA, generic small-cap).
/// </summary>
public class FilingWebsiteExtractorTests
{
    [Fact]
    public void PlainDisclosure_ReturnsHost()
    {
        var text = "Our website address is www.acme.com. We make our reports available there.";

        FilingWebsiteExtractor.Extract(text).Should().Be("www.acme.com");
    }

    [Fact]
    public void AppleStyle_CorporateAndIrHosts_PrefersCorporate()
    {
        var text =
            "The Company periodically provides certain information for investors on its "
            + "corporate website, www.apple.com, and its investor relations website, "
            + "investor.apple.com.";

        FilingWebsiteExtractor.Extract(text).Should().Be("www.apple.com");
    }

    [Fact]
    public void IrHostDisclosedFirst_CorporateHostStillWins()
    {
        var text =
            "We use our investor relations website, investor.nvidia.com, to publish "
            + "material information. Our company website is www.nvidia.com.";

        FilingWebsiteExtractor.Extract(text).Should().Be("www.nvidia.com");
    }

    [Fact]
    public void IrOnlyDisclosure_FallsBackToIrHost()
    {
        var text =
            "We announce material financial information to our investors using our "
            + "investor relations website, ir.acme.com, press releases and SEC filings.";

        FilingWebsiteExtractor.Extract(text).Should().Be("ir.acme.com");
    }

    [Fact]
    public void SecWebsiteReference_IsNeverReturned()
    {
        var text =
            "The SEC maintains a website that contains reports, proxy and information "
            + "statements at www.sec.gov. Our website address is www.acme.com.";

        FilingWebsiteExtractor.Extract(text).Should().Be("www.acme.com");
    }

    [Fact]
    public void OnlySecWebsite_ReturnsNull()
    {
        var text =
            "The SEC maintains a website that contains reports, proxy and information "
            + "statements regarding issuers at www.sec.gov.";

        FilingWebsiteExtractor.Extract(text).Should().BeNull();
    }

    [Fact]
    public void UrlWithoutSelfReferenceContext_ReturnsNull()
    {
        var text = "Reports are also distributed through www.businesswire.com to the public.";

        FilingWebsiteExtractor.Extract(text).Should().BeNull();
    }

    [Fact]
    public void SchemeAndPath_AreStripped()
    {
        var text = "Information is available on our website at https://www.acme.com/investors.";

        FilingWebsiteExtractor.Extract(text).Should().Be("www.acme.com");
    }

    [Fact]
    public void CompanyPossessive_WithParentheses_ReturnsHost()
    {
        var text = "Copies are posted on the Company's website (www.acme.com) without charge.";

        FilingWebsiteExtractor.Extract(text).Should().Be("www.acme.com");
    }

    [Fact]
    public void InternetAddressPhrasing_ReturnsHost()
    {
        var text =
            "Our Internet address is acme.com. Information on our website is not part of this report.";

        FilingWebsiteExtractor.Extract(text).Should().Be("acme.com");
    }

    [Fact]
    public void EmailAddressDomain_IsNotAWebsite()
    {
        var text = "Questions about our website may be sent to webmaster@acme.com by shareholders.";

        FilingWebsiteExtractor.Extract(text).Should().BeNull();
    }

    [Fact]
    public void TrailingSentencePunctuation_IsTrimmed()
    {
        var text =
            "These reports are available free of charge on our corporate website, www.acme.com.";

        FilingWebsiteExtractor.Extract(text).Should().Be("www.acme.com");
    }

    [Fact]
    public void HostIsLowerCased()
    {
        var text = "Our website address is WWW.Acme.COM and reports are available there.";

        FilingWebsiteExtractor.Extract(text).Should().Be("www.acme.com");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("No URLs anywhere in this text.")]
    public void MissingOrUrlFreeText_ReturnsNull(string text)
    {
        FilingWebsiteExtractor.Extract(text).Should().BeNull();
    }

    [Fact]
    public void RegistrantPhrasing_ReturnsHost()
    {
        var text = "The Registrant maintains a website at www.acme.com where filings are posted.";

        FilingWebsiteExtractor.Extract(text).Should().Be("www.acme.com");
    }
}

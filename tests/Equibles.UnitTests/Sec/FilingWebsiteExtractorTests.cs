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

    [Theory]
    [InlineData(
        "Our products are sold online, including through our website and e-commerce platforms such as Amazon.com."
    )]
    [InlineData(
        "From time to time, we may use our website, our Facebook page at https://www.facebook.com/acme and our X account to disclose information."
    )]
    [InlineData(
        "You may vote over the Internet on our proxy agent’s website at www.cstproxyvote.com, by following the instructions."
    )]
    [InlineData("We sell our products through the retailer’s website at Amazon.com and in stores.")]
    [InlineData(
        "Our transfer agent is Continental Stock Transfer and Trust Company, whose website is www.continentalstock.com."
    )]
    [InlineData(
        "Our transfer agent is Computershare Trust Company, N.A.; its website is www.computershare.com."
    )]
    [InlineData("Our transfer agent is Computershare, whose website is www.computershare.com.")]
    [InlineData("We use Computershare, whose website is www.computershare.com.")]
    [InlineData("Computershare is our transfer agent, and its website is www.computershare.com.")]
    [InlineData("We file reports with the SEC, and the SEC maintains a website at www.sec.gov.")]
    [InlineData(
        "Transfer Agent Computershare Inc., 480 Washington Blvd., Jersey City, New Jersey 07310, is the transfer agent and registrar for our stock. Its website is www.computershare.com and its telephone number is listed below."
    )]
    [InlineData(
        "The Trustee will make the Monthly Report available via its website. The Trustee’s website shall initially be located at https://pivot.usbank.com."
    )]
    public void ThirdPartySiteNearASelfReference_IsNotTheCompanyWebsite(string text)
    {
        FilingWebsiteExtractor.Extract(text).Should().BeNull();
    }

    [Fact]
    public void ThirdPartySiteBeforeTheDisclosure_DoesNotWin()
    {
        var text =
            "Our satellite radios are sold directly to consumers through our website, and indirectly "
            + "through online retailers, such as Amazon.com. Available Information. Our website "
            + "address is www.acme.com.";

        FilingWebsiteExtractor.Extract(text).Should().Be("www.acme.com");
    }

    [Theory]
    [InlineData("Our website address is *www.acme.com*, where reports are posted.")]
    [InlineData(
        "Our telephone number is (702) 944-5599, and our website is http://*www.acme.com*."
    )]
    [InlineData(
        "Filings are available at our web site under the investor relations link at https://www.acme.com."
    )]
    [InlineData(
        "The Company makes its filings available free of charge on its website at www.acme.com."
    )]
    [InlineData(
        "All material information is disclosed by the Fund on its website at www.acme.com."
    )]
    [InlineData("Acme Inc. (the “Company”) posts its reports on its website at www.acme.com.")]
    [InlineData("We post each director’s biography on our website at www.acme.com.")]
    [InlineData("We sell our product lines on our websites at www.acme.com and other channels.")]
    [InlineData(
        "We maintain a website at www.acme.com. The information there is not part of this report."
    )]
    [InlineData("We maintain an Internet website at www.acme.com, where reports are posted.")]
    [InlineData("We also maintain a corporate website at *www.acme.com*.")]
    public void DisclosureWordings_ReturnHost(string text)
    {
        FilingWebsiteExtractor.Extract(text).Should().Be("www.acme.com");
    }

    [Fact]
    public void ItsAfterASentenceNamingTheCompany_BelongsToTheThirdParty()
    {
        var text =
            "The Company's transfer agent is Computershare. Its website is www.computershare.com.";

        FilingWebsiteExtractor.Extract(text).Should().BeNull();
    }

    [Theory]
    [InlineData("amazon.com", true)]
    [InlineData("www.amazon.com", true)]
    [InlineData("www.acme.com", false)]
    [InlineData("ir.acme.com", false)]
    [InlineData("other.com", false)]
    public void Disowns_OnlyASiteMentionedNextToASelfReferenceButNeverNamed(
        string host,
        bool disowned
    )
    {
        var text =
            "Our products are sold through our website and e-commerce platforms such as "
            + "Amazon.com. Our website address is www.acme.com.";

        FilingWebsiteExtractor.Disowns(text, host).Should().Be(disowned);
    }

    [Fact]
    public void Disowns_IsFalseWhenAnotherMentionNamesTheSite()
    {
        var text =
            "Products are sold through our website and Acme.com stores. Our website address is "
            + "www.acme.com.";

        FilingWebsiteExtractor.Disowns(text, "acme.com").Should().BeFalse();
    }
}

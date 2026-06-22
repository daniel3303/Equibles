using Equibles.CommonStocks.HostedService.Services;

namespace Equibles.UnitTests.CommonStocks;

public class InvestorRelationsLinkExtractorPrimaryFirstUnderCapTests
{
    // Contract: Extract returns candidates "primary signals first ... capped at max", regardless
    // of document order. A weaker secondary signal (a SEC-filings link) appears BEFORE a strong
    // primary signal (an "Investors" link); with max:1 the cap must keep the primary candidate,
    // not the secondary one that happened to come first in the markup.
    [Fact]
    public void Extract_SecondaryAnchorPrecedesPrimaryUnderCap_KeepsThePrimary()
    {
        const string html =
            "<a href=\"/sec-filings\">SEC Filings</a>" + "<a href=\"/investors\">Investors</a>";

        var links = InvestorRelationsLinkExtractor.Extract(html, "https://acme.com", max: 1);

        links.Should().ContainSingle().Which.Should().Be("https://acme.com/investors");
    }
}

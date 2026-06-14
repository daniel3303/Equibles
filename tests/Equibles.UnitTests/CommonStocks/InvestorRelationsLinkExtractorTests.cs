using Equibles.CommonStocks.HostedService.Services;

namespace Equibles.UnitTests.CommonStocks;

public class InvestorRelationsLinkExtractorTests
{
    [Fact]
    public void Extract_FindsIrLinksByTextOrHref_PrimaryBeforeSecondary()
    {
        const string html =
            "<html><body><nav>"
            + "<a href=\"/about\">About us</a>"
            // primary: anchor text contains "investor", on a third-party host
            + "<a href=\"https://firstmid.q4ir.com/ir-home\">Investor Relations</a>"
            // primary: href path carries an "ir" segment (locale-prefixed), non-English text
            + "<a href=\"/ja/ir/\">株主・投資家</a>"
            // secondary: a SEC-filings link (SPAC/microcap pattern)
            + "<a href=\"/sec-filings/\">SEC Filings</a>"
            + "<a href=\"mailto:ir@acme.com\">Email IR</a>"
            + "<a href=\"https://www.sec.gov/edgar\">EDGAR</a>"
            + "<a href=\"#investors\">Skip to investors</a>"
            + "</nav></body></html>";

        var links = InvestorRelationsLinkExtractor.Extract(html, "https://acme.com");

        links
            .Should()
            .ContainInOrder(
                "https://firstmid.q4ir.com/ir-home",
                "https://acme.com/ja/ir/",
                "https://acme.com/sec-filings/"
            );
        // mailto, sec.gov, and the in-page fragment are excluded.
        links.Should().NotContain(l => l.Contains("sec.gov"));
        links.Should().NotContain(l => l.Contains("mailto") || l.Contains("#"));
    }

    [Fact]
    public void Extract_InvestorSubdomainAnchor_IsFound()
    {
        var links = InvestorRelationsLinkExtractor.Extract(
            "<a href=\"https://investors.acme.com/\">Investors</a>",
            "https://www.acme.com"
        );

        links.Should().ContainSingle().Which.Should().Be("https://investors.acme.com/");
    }

    [Fact]
    public void Extract_NoIrAnchors_ReturnsEmpty()
    {
        InvestorRelationsLinkExtractor
            .Extract("<html><body><a href=\"/about\">About</a></body></html>", "https://acme.com")
            .Should()
            .BeEmpty();
    }

    [Fact]
    public void Extract_UnparseableBase_ReturnsEmpty()
    {
        InvestorRelationsLinkExtractor
            .Extract("<a href=\"/investors\">Investors</a>", "not-a-url")
            .Should()
            .BeEmpty();
    }

    [Fact]
    public void Extract_DedupesAndCaps()
    {
        var html = string.Concat(
            Enumerable.Range(0, 10).Select(i => $"<a href=\"/investor-{i}\">Investors {i}</a>")
        );

        InvestorRelationsLinkExtractor
            .Extract(html, "https://acme.com", max: 3)
            .Should()
            .HaveCount(3);
    }
}

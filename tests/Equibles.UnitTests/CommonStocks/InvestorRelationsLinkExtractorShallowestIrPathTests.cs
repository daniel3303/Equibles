using Equibles.CommonStocks.HostedService.Services;

namespace Equibles.UnitTests.CommonStocks;

public class InvestorRelationsLinkExtractorShallowestIrPathTests
{
    // Contract: among IR candidates on the same ir. host, the shallowest path (the landing /
    // overview page) must outrank a deep article / press-release detail page — even when the
    // deep article appears first in the markup. Downstream takes the first candidate that
    // validates, so a deep "latest news" article that happens to come first must not become the
    // canonical IR URL (GH-5018).
    [Fact]
    public void Extract_DeepArticleBeforeLandingOnSameIrHost_RanksLandingFirst()
    {
        const string html =
            // deep press-release article on the ir. host appears FIRST in the DOM
            "<a href=\"https://ir.acme.com/news/news-release-details/2026/Acme-Update/default.aspx\">Latest news</a>"
            // IR landing / overview appears later
            + "<a href=\"https://ir.acme.com/overview/default.aspx\">Investor Relations</a>";

        var links = InvestorRelationsLinkExtractor.Extract(html, "https://acme.com");

        links.First().Should().Be("https://ir.acme.com/overview/default.aspx");
    }
}

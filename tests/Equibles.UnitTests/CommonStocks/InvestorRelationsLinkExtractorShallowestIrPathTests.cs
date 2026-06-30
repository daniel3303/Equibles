using Equibles.CommonStocks.HostedService.Services;

namespace Equibles.UnitTests.CommonStocks;

public class InvestorRelationsLinkExtractorShallowestIrPathTests
{
    // Contract (#5018): among primary candidates the shallowest IR path wins, so an IR
    // landing/section root outranks a deep article/detail page on the same IR host. Here a
    // homepage "latest news" link to a deep press-release-details article appears BEFORE the IR
    // overview landing; without shallowest-first ordering the deep article (first in document
    // order) would be probed first and stored as the canonical IR URL.
    [Fact]
    public void Extract_DeepArticleBeforeLandingOnIrHost_RanksLandingFirst()
    {
        const string html =
            "<a href=\"https://ir.acme.com/news/press-release-details/2026/Acme-Reports-Q4/default.aspx\">Latest news</a>"
            + "<a href=\"https://ir.acme.com/overview/default.aspx\">Investor Relations</a>";

        var links = InvestorRelationsLinkExtractor.Extract(html, "https://www.acme.com");

        links
            .Should()
            .ContainInOrder(
                "https://ir.acme.com/overview/default.aspx",
                "https://ir.acme.com/news/press-release-details/2026/Acme-Reports-Q4/default.aspx"
            );
    }
}

using Equibles.CommonStocks.HostedService.Services;

namespace Equibles.UnitTests.CommonStocks;

public class InvestorRelationsLinkExtractorDeepArticleDemotedTests
{
    // Contract (GH-5018): on an `ir.` host both a deep press-release/news article detail page and
    // the IR landing/overview page validate as IR pages, so "first that validates wins" downstream
    // would store whichever the homepage links first — often a one-off dated article. Extract must
    // rank the shallow IR landing ahead of the deep article so the landing becomes the canonical
    // IR URL; the article stays as a fallback (still returned, just last), so a stock whose only
    // IR link is an article is unaffected.
    [Fact]
    public void Extract_DeepArticleBeforeLandingOnIrHost_RanksLandingFirst()
    {
        // The article anchor appears BEFORE the landing anchor in the document — under the old
        // document-order behavior the article would win.
        const string html = """
            <html><body>
            <a href="https://ir.acme.com/news/news-release-details/2026/Acme-Reports-Q1/default.aspx">Latest news</a>
            <a href="https://ir.acme.com/overview/default.aspx">Investor overview</a>
            </body></html>
            """;

        var links = InvestorRelationsLinkExtractor.Extract(html, "https://acme.com");

        links[0].Should().Be("https://ir.acme.com/overview/default.aspx");
        links
            .Should()
            .Contain(
                "https://ir.acme.com/news/news-release-details/2026/Acme-Reports-Q1/default.aspx"
            );
    }
}

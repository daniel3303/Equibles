using Equibles.CommonStocks.HostedService.Services;

namespace Equibles.UnitTests.CommonStocks;

/// <summary>
/// Adversarial: <see cref="InvestorRelationsLinkExtractor.Extract"/> promises absolute
/// http(s) IR candidates, and its href resolver rejects non-navigable schemes
/// (<c>mailto:</c>, <c>tel:</c>, <c>javascript:</c>, in-page <c>#</c>). A JS-driven
/// "Investor Relations" anchor (<c>href="javascript:..."</c>) carries a strong primary
/// keyword in its text, so without the scheme guard it would be surfaced as a candidate
/// and later probed as a garbage URL. The existing suite pins the <c>mailto:</c>/<c>#</c>
/// rejection but not <c>javascript:</c>.
/// </summary>
public class InvestorRelationsLinkExtractorJavascriptHrefTests
{
    [Fact]
    public void Extract_JavascriptHrefWithIrText_IsRejectedKeepingOnlyTheRealCandidate()
    {
        var html = """
            <html><body>
            <a href="javascript:openInvestors()">Investor Relations</a>
            <a href="/investors">Investors</a>
            </body></html>
            """;

        var links = InvestorRelationsLinkExtractor.Extract(html, "https://acme.com");

        links.Should().ContainSingle().Which.Should().Be("https://acme.com/investors");
    }
}

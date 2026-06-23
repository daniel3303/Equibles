using Equibles.CommonStocks.HostedService.Services;

namespace Equibles.UnitTests.CommonStocks;

public class InvestorRelationsLinkExtractorHostSubdomainTests
{
    // Contract: an `ir.` / `investor(s).` host subdomain is a strong IR signal on its own —
    // the link must be classified even when the anchor text carries no English IR keyword
    // (a regional/localised IR portal). The other tests classify via keyword text; this pins
    // the host-subdomain leg of HasIrHostOrPath, which exists precisely for non-English anchors.
    [Fact]
    public void Extract_AnchorOnIrSubdomainWithNonEnglishText_IsReturnedAsCandidate()
    {
        const string html = """
            <html><body>
            <a href="https://ir.acme.com/">投資家情報</a>
            </body></html>
            """;

        var links = InvestorRelationsLinkExtractor.Extract(html, "https://acme.com");

        links.Should().ContainSingle().Which.Should().Be("https://ir.acme.com/");
    }
}

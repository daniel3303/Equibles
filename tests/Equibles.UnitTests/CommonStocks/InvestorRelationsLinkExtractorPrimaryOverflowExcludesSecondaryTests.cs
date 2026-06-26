using Equibles.CommonStocks.HostedService.Services;

namespace Equibles.UnitTests.CommonStocks;

public class InvestorRelationsLinkExtractorPrimaryOverflowExcludesSecondaryTests
{
    // Contract: candidates are returned primary signals first, capped at max. When primary
    // anchors alone fill the cap, the weaker secondary anchors must be excluded entirely — even
    // one that appears earlier in the document — and the result is the first max primary links
    // in document order. Existing tests cover the all-primary cap and the under-cap ordering;
    // this pins the combination where the cap is binding and secondary links are present.
    [Fact]
    public void Extract_PrimaryAnchorsOverflowCap_ExcludesSecondaryEntirely()
    {
        const string html = """
            <a href="/sec-filings">SEC Filings</a>
            <a href="/investor-1">Investors 1</a>
            <a href="/investor-2">Investors 2</a>
            <a href="/investor-3">Investors 3</a>
            <a href="/investor-4">Investors 4</a>
            <a href="/financial-reports">Annual Report</a>
            """;

        var result = InvestorRelationsLinkExtractor.Extract(html, "https://acme.com", max: 3);

        result
            .Should()
            .Equal(
                "https://acme.com/investor-1",
                "https://acme.com/investor-2",
                "https://acme.com/investor-3"
            );
    }
}

using Equibles.CommonStocks.HostedService.Services;

namespace Equibles.UnitTests.CommonStocks;

public class InvestorRelationsLinkExtractorDedupSuppressesLaterIrAnchorTests
{
    // Contract: Extract returns the URL of any anchor whose visible text or href is about
    // investor relations. De-duplication must apply among the CANDIDATES, not across every
    // resolved anchor — a non-IR anchor pointing at the same URL must not consume the dedup
    // slot and silently suppress a later IR anchor to that URL. Here a generic "Company
    // Overview" link and an "Investor Relations" link share /overview; the page clearly has an
    // IR link, so /overview must be returned.
    [Fact(
        Skip = "GH-3957 — dedup key recorded before IR classification drops a valid later IR link"
    )]
    public void Extract_NonIrAnchorPrecedesIrAnchorToSameUrl_StillReturnsTheIrCandidate()
    {
        const string html =
            "<a href=\"/overview\">Company Overview</a>"
            + "<a href=\"/overview\">Investor Relations</a>";

        var links = InvestorRelationsLinkExtractor.Extract(html, "https://acme.com");

        links.Should().ContainSingle().Which.Should().Be("https://acme.com/overview");
    }
}

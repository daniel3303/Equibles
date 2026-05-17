using Equibles.Congress.HostedService.Services;

namespace Equibles.UnitTests.Congress;

public class DisclosureParsingHelperIsValidUrlBoundaryTests
{
    // Contract: IsValidDisclosureUrl guards which URLs the scraper will fetch —
    // it must accept only URLs that actually belong to the expected disclosure
    // host. An attacker-registered domain that merely *prefixes* the expected
    // base ("...house.gov.evil.example") is NOT on the house.gov origin and
    // must be rejected, or the guard is an SSRF bypass.
    [Fact]
    public void IsValidDisclosureUrl_AttackerDomainWithBaseAsPrefix_ReturnsFalse()
    {
        var result = DisclosureParsingHelper.IsValidDisclosureUrl(
            "https://disclosures-clerk.house.gov.evil.example/malware.pdf",
            "https://disclosures-clerk.house.gov"
        );

        result.Should().BeFalse();
    }
}

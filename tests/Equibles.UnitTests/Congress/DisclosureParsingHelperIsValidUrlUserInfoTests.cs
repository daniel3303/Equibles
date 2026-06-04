using Equibles.Congress.HostedService.Services;

namespace Equibles.UnitTests.Congress;

public class DisclosureParsingHelperIsValidUrlUserInfoTests
{
    // Contract: "scheme, host and port must match exactly." Existing tests pin the
    // host-prefix, scheme-downgrade, port-mismatch and null vectors. The classic
    // origin-check bypass still unpinned is the userinfo trick: an attacker URL that
    // embeds the legitimate host in the credentials segment
    // ("https://legit-host@evil.example/") — the real origin host is evil.example,
    // so the guard must reject it. A naive string match on the base would be fooled.
    [Fact]
    public void IsValidDisclosureUrl_LegitimateHostInUserInfoButAttackerHost_ReturnsFalse()
    {
        const string baseUrl = "https://disclosures-clerk.house.gov";

        var bypass = DisclosureParsingHelper.IsValidDisclosureUrl(
            "https://disclosures-clerk.house.gov@evil.example/public_disc/2025FD.zip",
            baseUrl
        );
        var legitimate = DisclosureParsingHelper.IsValidDisclosureUrl(
            "https://disclosures-clerk.house.gov/public_disc/financial-pdfs/2025FD.zip",
            baseUrl
        );

        bypass
            .Should()
            .BeFalse(
                "the real origin host is evil.example; the base host in userinfo is not the host"
            );
        legitimate
            .Should()
            .BeTrue("the exact https origin is still accepted (predicate is not vacuous)");
    }
}

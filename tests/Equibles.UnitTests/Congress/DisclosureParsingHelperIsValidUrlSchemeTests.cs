using Equibles.Congress.HostedService.Services;

namespace Equibles.UnitTests.Congress;

public class DisclosureParsingHelperIsValidUrlSchemeTests
{
    // Contract: "scheme, host and port must match exactly." Existing tests pin
    // the host-prefix SSRF bypass and the null case, but NOT the scheme half.
    // A URL on the exact correct host with the scheme downgraded https→http is
    // a classic SSRF/MITM vector the guard must reject — while the legitimate
    // https origin must still be accepted (proves the predicate isn't vacuous).
    [Fact]
    public void IsValidDisclosureUrl_SchemeDowngradedToHttpOnExactHost_ReturnsFalse()
    {
        const string baseUrl = "https://disclosures-clerk.house.gov";

        var downgraded = DisclosureParsingHelper.IsValidDisclosureUrl(
            "http://disclosures-clerk.house.gov/public_disc/financial-pdfs/2025FD.zip",
            baseUrl
        );
        var legitimate = DisclosureParsingHelper.IsValidDisclosureUrl(
            "https://disclosures-clerk.house.gov/public_disc/financial-pdfs/2025FD.zip",
            baseUrl
        );

        downgraded
            .Should()
            .BeFalse("an http:// downgrade is not the https origin and must be rejected");
        legitimate.Should().BeTrue("the exact https origin is the only allowed source");
    }
}

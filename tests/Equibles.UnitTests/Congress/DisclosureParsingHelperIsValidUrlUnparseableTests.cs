using Equibles.Congress.HostedService.Services;

namespace Equibles.UnitTests.Congress;

public class DisclosureParsingHelperIsValidUrlUnparseableTests
{
    [Fact]
    public void IsValidDisclosureUrl_UnparseableAbsoluteUrl_ReturnsFalse()
    {
        // Contract: this is a security allowlist predicate — it must be total
        // and return false on anything that can't be parsed as an absolute URI
        // (no scheme/host), without NRE-ing on `parsed.Scheme`. A regression
        // that dropped the Uri.TryCreate guard would crash the scraper on any
        // junk href the upstream HTML happens to contain.
        var result = DisclosureParsingHelper.IsValidDisclosureUrl(
            "not a url",
            "https://disclosures.house.gov"
        );

        result.Should().BeFalse();
    }
}

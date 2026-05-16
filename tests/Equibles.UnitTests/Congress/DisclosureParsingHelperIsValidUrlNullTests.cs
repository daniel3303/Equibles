using Equibles.Congress.HostedService.Services;

namespace Equibles.UnitTests.Congress;

public class DisclosureParsingHelperIsValidUrlNullTests
{
    [Fact(Skip = "GH-724 — IsValidDisclosureUrl NREs on null url instead of returning false")]
    public void IsValidDisclosureUrl_NullUrl_ReturnsFalseInsteadOfThrowing()
    {
        // Contract: this is a security allowlist predicate — it must be total and
        // return false for anything not under the expected base, including null.
        // A null href is reachable from the scraper's HTML parsing pipeline.
        var result = DisclosureParsingHelper.IsValidDisclosureUrl(
            null,
            "https://disclosures.house.gov"
        );

        result
            .Should()
            .BeFalse(
                "a URL-allowlist guard must reject null as invalid, not throw a "
                    + "NullReferenceException that crashes (or is swallowed by) the caller"
            );
    }
}

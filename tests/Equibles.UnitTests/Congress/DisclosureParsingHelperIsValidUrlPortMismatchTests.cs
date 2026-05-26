using Equibles.Congress.HostedService.Services;

namespace Equibles.UnitTests.Congress;

public class DisclosureParsingHelperIsValidUrlPortMismatchTests
{
    // IsValidDisclosureUrl is the SSRF guard for House/Senate disclosure
    // fetches. The body explicitly compares scheme, host, AND port — siblings
    // pin the scheme and host arms; this pin guards the port. A non-default
    // port (8443) on the otherwise-correct host is a classic SSRF surface:
    // an attacker who can plant a URL pointing at "https://disclosures.
    // house.gov:8443/..." (a malicious service co-located behind the same
    // domain via a proxy or split-horizon DNS) would exfiltrate through a
    // port that is NOT what the disclosure service uses. A refactor that
    // dropped `parsed.Port == baseUri.Port` would silently widen the
    // allowlist to all ports on the base host.
    [Fact]
    public void IsValidDisclosureUrl_SameHostDifferentPort_ReturnsFalse()
    {
        var result = DisclosureParsingHelper.IsValidDisclosureUrl(
            "https://disclosures.house.gov:8443/public_disc/ptr-pdfs/2024/file.pdf",
            "https://disclosures.house.gov"
        );

        result.Should().BeFalse();
    }
}

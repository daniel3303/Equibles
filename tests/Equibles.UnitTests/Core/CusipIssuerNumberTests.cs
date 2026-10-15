using Equibles.Core.Identity;
using FluentAssertions;

namespace Equibles.UnitTests.Core;

public class CusipIssuerNumberTests
{
    [Theory]
    [InlineData("KYG5924H1231", "KYG5924H")]
    [InlineData("KYG5924H1561", "KYG5924H")]
    [InlineData("BMG8114P2022", "BMG8114P")]
    public void ACusipNumberedJurisdictionYieldsItsIssuerNumber(string isin, string issuerNumber) =>
        InternationalSecurityIdentifiers.CusipIssuerNumber(isin).Should().Be(issuerNumber);

    // A national agency's sequential number embeds no issuer, and US CUSIPs keep their own SEC identity paths.
    [Theory]
    [InlineData("HK0000050093")]
    [InlineData("CNE1000003G1")]
    [InlineData("US02079K3059")]
    public void AnotherNumberingAgencyYieldsNone(string isin) =>
        InternationalSecurityIdentifiers.CusipIssuerNumber(isin).Should().BeNull();

    // KYG5924H1231 with its CINS check digit changed and the ISIN check digit recomputed to stay a valid ISIN.
    [Theory]
    [InlineData("KYG5924H1249")]
    [InlineData("KYG5924H123")]
    [InlineData(null)]
    public void AnEmbeddedCinsWithoutAValidCheckDigitYieldsNone(string isin) =>
        InternationalSecurityIdentifiers.CusipIssuerNumber(isin).Should().BeNull();
}

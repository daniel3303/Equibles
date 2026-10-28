using Equibles.Core.Identity;
using FluentAssertions;

namespace Equibles.UnitTests.Core;

public class IndianIssuerCodeTests
{
    // HDFC Bank before and after its 2019 split, and a DVR line and a rights entitlement beside their companies' shares.
    [Theory]
    [InlineData("INE040A01026", "INE040A")]
    [InlineData("INE040A01034", "INE040A")]
    [InlineData("INE224E01036", "INE224E")]
    [InlineData("INE637H20024", "INE637H")]
    public void AnIndianIsinYieldsItsIssuerCode(string isin, string issuerCode) =>
        InternationalSecurityIdentifiers.IndianIssuerCode(isin).Should().Be(issuerCode);

    // INE040A01034 with its check digit changed, another jurisdiction's ISIN, and no ISIN.
    [Theory]
    [InlineData("INE040A01027")]
    [InlineData("KYG5924H1231")]
    [InlineData("INE040A0103")]
    [InlineData(null)]
    public void AnythingButAValidIndianIsinYieldsNone(string isin) =>
        InternationalSecurityIdentifiers.IndianIssuerCode(isin).Should().BeNull();
}

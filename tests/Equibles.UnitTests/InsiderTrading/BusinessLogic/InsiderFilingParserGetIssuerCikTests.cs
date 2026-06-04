using Equibles.InsiderTrading.BusinessLogic;

namespace Equibles.UnitTests.InsiderTrading.BusinessLogic;

public class InsiderFilingParserGetIssuerCikTests
{
    // GetIssuerCik lets the filing processor confirm a Form 4 surfaced by a CIK feed
    // actually belongs to the company being processed. EDGAR lists a Form 4 under every
    // CIK it references (issuer + each reporting owner), so a public-company insider that
    // sold another issuer's stock would otherwise be attributed to itself. The issuer CIK
    // is zero-padded in the XML but stored un-padded on the company, so the leading zeros
    // must be stripped for the comparison to hold.

    [Fact]
    public void GetIssuerCik_PaddedIssuerCik_StripsLeadingZeros()
    {
        var root = InsiderFilingParser.TryGetOwnershipRoot(
            "<ownershipDocument><issuer><issuerCik>0002046386</issuerCik></issuer></ownershipDocument>"
        );

        InsiderFilingParser.GetIssuerCik(root).Should().Be("2046386");
    }

    [Fact]
    public void GetIssuerCik_NoIssuerBlock_ReturnsNull()
    {
        var root = InsiderFilingParser.TryGetOwnershipRoot(
            "<ownershipDocument><reportingOwner /></ownershipDocument>"
        );

        InsiderFilingParser.GetIssuerCik(root).Should().BeNull();
    }

    [Fact]
    public void GetIssuerCik_EmptyIssuerCik_ReturnsNull()
    {
        var root = InsiderFilingParser.TryGetOwnershipRoot(
            "<ownershipDocument><issuer><issuerCik></issuerCik></issuer></ownershipDocument>"
        );

        InsiderFilingParser.GetIssuerCik(root).Should().BeNull();
    }
}

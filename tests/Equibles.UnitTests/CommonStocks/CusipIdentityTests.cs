using Equibles.CommonStocks.Data.Helpers;
using Xunit;

namespace Equibles.UnitTests.CommonStocks;

/// <summary>
/// A CUSIP's first 6 characters identify the ISSUER and the next 2 the specific issue.
/// The retired-CUSIP sweep leans on that to stay safe against ticker recycling: a symbol
/// freed by a delisted issuer and reassigned years later would otherwise pull the dead
/// issuer's CUSIP — and its 13F positions — onto whichever company holds the symbol now.
/// </summary>
public class CusipIdentityTests
{
    [Fact]
    public void AmcAcrossItsReverseSplitIsOneIssuer()
    {
        // 00165C104 (pre-2023) and 00165C302 (current) differ only in the issue digits.
        CusipIdentity.SameIssuer("00165C104", "00165C302").Should().BeTrue();
    }

    [Fact]
    public void MerckAcrossItsMergerIsNotOneIssuer()
    {
        // 589331107 → 58933Y105 moved the issuer prefix itself, so the sweep declines to
        // link them. Coverage loss over a wrong link — deliberate.
        CusipIdentity.SameIssuer("589331107", "58933Y105").Should().BeFalse();
    }

    [Fact]
    public void UnrelatedIssuersDoNotMatch()
    {
        CusipIdentity.SameIssuer("037833100", "594918104").Should().BeFalse();
    }

    [Fact]
    public void CasingAndSurroundingWhitespaceDoNotChangeTheAnswer()
    {
        CusipIdentity.SameIssuer(" 00165c104 ", "00165C302").Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0016")]
    public void AValueTooShortToCarryAnIssuerNeverMatches(string cusip)
    {
        // An unknown issuer must never be assumed to match — that is the whole guard.
        CusipIdentity.SameIssuer(cusip, "00165C302").Should().BeFalse();
        CusipIdentity.SameIssuer("00165C302", cusip).Should().BeFalse();
        CusipIdentity.Issuer(cusip).Should().BeNull();
    }

    [Fact]
    public void TheIssuerIsTheFirstSixCharactersUpperCased()
    {
        CusipIdentity.Issuer("00165c104").Should().Be("00165C");
    }
}

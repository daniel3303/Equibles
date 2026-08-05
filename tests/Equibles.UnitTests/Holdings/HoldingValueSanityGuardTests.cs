using System.Globalization;
using Equibles.Holdings.HostedService.Services;
using FluentAssertions;

namespace Equibles.UnitTests.Holdings;

public class HoldingValueSanityGuardTests
{
    // ── IsImplausibleClose ─────────────────────────────────────────────

    [Theory]
    [InlineData("800000", false)] // BRK-A territory — must always stay valid
    [InlineData("1000000", false)] // the ceiling itself is still allowed
    [InlineData("1000000.01", true)]
    [InlineData("285249984", true)] // the corrupt close that minted $102.8T of phantom value
    [InlineData("0.0008", false)] // sub-penny OTC closes are legitimate
    public void IsImplausibleClose_BoundsAtOneMillionPerShare(string close, bool expected)
    {
        HoldingValueSanityGuard
            .IsImplausibleClose(decimal.Parse(close, CultureInfo.InvariantCulture))
            .Should()
            .Be(expected);
    }

    // ── GrosslyExceedsFiled ────────────────────────────────────────────

    [Fact]
    public void GrosslyExceedsFiled_NoFiledValue_NeverTrips()
    {
        HoldingValueSanityGuard.GrosslyExceedsFiled(1_000_000_000m, null).Should().BeFalse();
        HoldingValueSanityGuard.GrosslyExceedsFiled(1_000_000_000m, 0L).Should().BeFalse();
        HoldingValueSanityGuard.GrosslyExceedsFiled(1_000_000_000m, -5L).Should().BeFalse();
    }

    [Theory]
    [InlineData("5000000", false)] // exactly 5× the filed 1M — ordinary drift ceiling, allowed
    [InlineData("5000001", true)] // just past the cap
    [InlineData("2000000", false)] // 2× — a stale mark, not a units error
    public void GrosslyExceedsFiled_BoundsAtFiveTimesFiled(string derived, bool expected)
    {
        HoldingValueSanityGuard
            .GrosslyExceedsFiled(decimal.Parse(derived, CultureInfo.InvariantCulture), 1_000_000L)
            .Should()
            .Be(expected);
    }

    [Fact]
    public void GrosslyExceedsFiled_CorruptCloseSignature_Trips()
    {
        // The production case: 273,201 shares × a $50M close against a filed $1.09M —
        // a 12.5-million-fold inflation that rendered as 76.7% of the filer's portfolio.
        var derived = 273_201m * 50_000_000m;
        HoldingValueSanityGuard.GrosslyExceedsFiled(derived, 1_092_804L).Should().BeTrue();
    }
}

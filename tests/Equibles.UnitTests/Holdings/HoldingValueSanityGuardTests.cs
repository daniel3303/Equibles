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

    // ── FiledLooksThousandsScaled ──────────────────────────────────────

    [Theory]
    [InlineData("1000000000", true)] // exactly 1,000× — the canonical thousands filer
    [InlineData("900000000", true)] // band floor (900×) — mark drift on top of the unit
    [InlineData("1100000000", true)] // band ceiling (1,100×)
    [InlineData("899999999", false)] // just under the floor — a real basis error, not units
    [InlineData("1100000001", false)] // just past the ceiling — derivation-side error
    [InlineData("5000001", false)] // ordinary gross disagreement (5×+) stays a derivation fault
    public void FiledLooksThousandsScaled_RatioInsideTightBand_ReturnsTrue(
        string derived,
        bool expected
    )
    {
        HoldingValueSanityGuard
            .FiledLooksThousandsScaled(
                decimal.Parse(derived, CultureInfo.InvariantCulture),
                shares: 500_000L,
                filedValue: 1_000_000L
            )
            .Should()
            .Be(expected);
    }

    [Fact]
    public void FiledLooksThousandsScaled_SharesEqualFiledValue_ReturnsFalse()
    {
        // The duplicated share-count column (Corrupt13FShareCountRepairer's population): the
        // shares column IS the value column, so the derived/filed ratio equals the share
        // price. A $1,000 stock lands exactly in the band — the structural exclusion, not
        // the band, is what keeps it out.
        HoldingValueSanityGuard
            .FiledLooksThousandsScaled(1_000_000_000m, shares: 1_000_000L, filedValue: 1_000_000L)
            .Should()
            .BeFalse();
    }

    [Fact]
    public void FiledLooksThousandsScaled_NoFiledValue_ReturnsFalse()
    {
        HoldingValueSanityGuard
            .FiledLooksThousandsScaled(1_000_000_000m, 500_000L, null)
            .Should()
            .BeFalse();
        HoldingValueSanityGuard
            .FiledLooksThousandsScaled(1_000_000_000m, 500_000L, 0L)
            .Should()
            .BeFalse();
    }

    // ── ShouldPublishFiledInsteadOfDerived ─────────────────────────────

    [Fact]
    public void ShouldPublishFiledInsteadOfDerived_ThousandsFiler_KeepsDerivation()
    {
        // The Baupost signature: 3,118,754 AMZN shares derive ~$650M against a filed
        // 649,543 (thousands). Publishing the filed figure served the book 1,000×
        // understated — the derivation must stand.
        var derived = 3_118_754m * 208.4m;
        HoldingValueSanityGuard
            .ShouldPublishFiledInsteadOfDerived(derived, 3_118_754L, 649_543L)
            .Should()
            .BeFalse();
    }

    [Fact]
    public void ShouldPublishFiledInsteadOfDerived_CorruptClose_PublishesFiled()
    {
        var derived = 273_201m * 50_000_000m;
        HoldingValueSanityGuard
            .ShouldPublishFiledInsteadOfDerived(derived, 273_201L, 1_092_804L)
            .Should()
            .BeTrue();
    }

    [Fact]
    public void ShouldPublishFiledInsteadOfDerived_DuplicatedShareCountRow_PublishesFiled()
    {
        // shares == filed value is the duplicated-column signature; even a ratio inside the
        // thousands band must publish the filed figure there.
        HoldingValueSanityGuard
            .ShouldPublishFiledInsteadOfDerived(1_000_000_000m, 1_000_000L, 1_000_000L)
            .Should()
            .BeTrue();
    }

    [Fact]
    public void ShouldPublishFiledInsteadOfDerived_CorruptCloseInsideBand_KeepsDerivation()
    {
        // The accepted trade-off, stated as a test: a genuinely corrupt close that inflates a
        // NON-duplicated row by ~1,000× is indistinguishable from a thousands filer and now
        // publishes the derivation. The tight 900×–1,100× band and the shares != filed
        // exclusion bound the exposure; a wider corruption (12.5M× production case) still
        // publishes filed.
        HoldingValueSanityGuard
            .ShouldPublishFiledInsteadOfDerived(1_000_000_000m, 500_000L, 1_000_000L)
            .Should()
            .BeFalse();
    }

    [Theory]
    [InlineData("5000000", false)] // 5× — inside ordinary drift, derivation publishes
    [InlineData("5000001", true)] // just past the cap and below the band — filed publishes
    [InlineData("899999999", true)] // 900×−ε — still a derivation fault
    [InlineData("1000000000", false)] // 1,000× — thousands basis, derivation publishes
    [InlineData("1100000001", true)] // past the band — derivation fault, filed publishes
    public void ShouldPublishFiledInsteadOfDerived_RatioAcrossBandEdges_PublishesFiledOutsideBandOnly(
        string derived,
        bool expected
    )
    {
        HoldingValueSanityGuard
            .ShouldPublishFiledInsteadOfDerived(
                decimal.Parse(derived, CultureInfo.InvariantCulture),
                shares: 500_000L,
                filedValue: 1_000_000L
            )
            .Should()
            .Be(expected);
    }
}

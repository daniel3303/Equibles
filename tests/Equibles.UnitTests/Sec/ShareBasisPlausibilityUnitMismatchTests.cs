using Equibles.Sec.FinancialFacts.BusinessLogic;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// Pins the unit-mismatch predicate both writers of CommonStock.SharesOutStanding share. It must
/// separate two regimes: legitimate same-unit divergences (corporate-action lags, at most a few
/// hundred x) where the EDGAR cover-page count must stay authoritative, and different-unit pairs
/// (ordinary shares vs ADSs, garbage counts — observed 458x to 80,000x) where reconciling onto the
/// EDGAR count inflates market cap by the full ratio while leaving the stored pair self-consistent,
/// invisible to the derived-price scan (AKTX stored $998B against a true ~$27M).
/// </summary>
public class ShareBasisPlausibilityUnitMismatchTests
{
    [Fact]
    public void IsUnitMismatch_OrdinarySharesAgainstAdsCount_Fires()
    {
        // AKTX's shape: the 10-Q cover page counts 91.57B ordinary shares while Yahoo's base is
        // the ~2.5M listed ADSs (80,000 ordinary per ADS) — a ~37,000x disagreement.
        ShareBasisPlausibility.IsUnitMismatch(91_567_009_533L, 2_477_000L).Should().BeTrue();
    }

    [Fact]
    public void IsUnitMismatch_GarbageSmallEdgarCount_FiresInTheOtherDirection()
    {
        // ABTC's shape: a stored cover-page count ~458x SMALLER than the listed-security base.
        // The predicate is direction-agnostic — either side may hold the larger figure.
        ShareBasisPlausibility.IsUnitMismatch(1_000_000L, 458_000_000L).Should().BeTrue();
    }

    [Fact]
    public void IsUnitMismatch_ReverseSplitLag_DoesNotFire()
    {
        // COPR's shape (#3575): Yahoo lags a 1-for-20 reverse split, so its base is 20x the
        // EDGAR count. EDGAR is RIGHT here — the rescale must proceed, the guard must stay out.
        ShareBasisPlausibility.IsUnitMismatch(5_000_000L, 100_000_000L).Should().BeFalse();
    }

    [Fact]
    public void IsUnitMismatch_MultiClassQuotedClassBase_DoesNotFire()
    {
        // GOOGL's shape: EDGAR's entity total vs Yahoo's quoted-class count differ ~2x. Same
        // unit, different scopes — the rescale handles this; the guard must not interfere.
        ShareBasisPlausibility.IsUnitMismatch(12_116_000_000L, 5_826_000_000L).Should().BeFalse();
    }

    [Fact]
    public void IsUnitMismatch_ExactThreshold_Fires()
    {
        // The boundary is inclusive: at exactly MaxPlausibleSameUnitRatio the pair is treated as
        // different units.
        var threshold = (long)ShareBasisPlausibility.MaxPlausibleSameUnitRatio;
        ShareBasisPlausibility.IsUnitMismatch(threshold * 1_000_000L, 1_000_000L).Should().BeTrue();
        ShareBasisPlausibility.IsUnitMismatch(1_000_000L, threshold * 1_000_000L).Should().BeTrue();
    }

    [Fact]
    public void IsUnitMismatch_JustUnderThreshold_DoesNotFire()
    {
        var justUnder = (long)ShareBasisPlausibility.MaxPlausibleSameUnitRatio - 1;
        ShareBasisPlausibility
            .IsUnitMismatch(justUnder * 1_000_000L, 1_000_000L)
            .Should()
            .BeFalse();
    }

    [Theory]
    [InlineData(0L, 1_000_000L)]
    [InlineData(1_000_000L, 0L)]
    [InlineData(-1L, 1_000_000L)]
    [InlineData(1_000_000L, -1L)]
    public void IsUnitMismatch_MissingCount_DoesNotFire(long countA, long countB)
    {
        // A missing (zero/negative) count is absence of evidence, not evidence of a mismatch.
        ShareBasisPlausibility.IsUnitMismatch(countA, countB).Should().BeFalse();
    }
}

using Equibles.Holdings.HostedService.Services;

namespace Equibles.UnitTests.Holdings;

/// <summary>
/// A holding's value is always derived as shares × price, so a share count in the wrong units is
/// multiplied straight into a dollar figure every AUM ranking then treats as real. The live case:
/// a Schedule 13D/A accurately reporting 32,098,694,296 Class A ordinary shares of NaaS — 55.9% of
/// the class — priced at the ADS quote, which is worth 3,200 ordinary shares, giving a $100.8B
/// position in a $38M company.
///
/// The guard's second job is knowing when NOT to fire. <c>SharesOutStanding</c> is itself corrupt
/// for a few stocks, and anchoring on it blindly deletes real data: Air Lease stores 200 shares
/// beside a correct $7.28B market cap, and a naive rule would have dropped its 7,309 legitimate
/// holdings — 94% of everything such a rule matches.
/// </summary>
public class ImpossiblePositionGuardTests
{
    // NaaS as stored: 12,055,201 ADS outstanding, $37.6M market cap ($3.12 implied per share).
    private const long NaasSharesOutstanding = 12_055_201;
    private const double NaasMarketCap = 37_600_000d;

    [Fact]
    public void APositionLargerThanTheIssuer_CannotBeValued()
    {
        ImpossiblePositionGuard
            .ExceedsTheIssuer(32_098_694_296L, NaasSharesOutstanding, NaasMarketCap)
            .Should()
            .BeTrue();
    }

    [Fact]
    public void AnOrdinaryMajorityStake_IsLeftAlone()
    {
        ImpossiblePositionGuard
            .ExceedsTheIssuer(6_700_000L, NaasSharesOutstanding, NaasMarketCap)
            .Should()
            .BeFalse("a 55% holder owns a legitimate slice of the company");
    }

    [Theory]
    // The multiple is deliberately loose: the share count is today's while the holding is a
    // quarter-end position, so a buyback or reverse split between them is ordinary.
    [InlineData(12_055_202L, false)] // barely above the count
    [InlineData(24_110_402L, false)] // exactly 2×, the boundary
    [InlineData(24_110_403L, true)] // past 2×
    public void TheToleranceAbsorbsAShareCountFromADifferentDate(long shares, bool expected)
    {
        ImpossiblePositionGuard
            .ExceedsTheIssuer(shares, NaasSharesOutstanding, NaasMarketCap)
            .Should()
            .Be(expected);
    }

    [Fact]
    public void ACorruptShareCount_DisablesTheGuardRatherThanDeletingRealHoldings()
    {
        // Air Lease as stored: 200 shares against a correct $7.28B market cap, implying $36M a
        // share. One of the two figures is broken and the guard cannot tell which, so it declines
        // to judge — every Air Lease holding keeps its value.
        ImpossiblePositionGuard
            .ExceedsTheIssuer(13_862_881L, 200L, 7_282_301_440d)
            .Should()
            .BeFalse();

        ImpossiblePositionGuard.AnchorIsTrustworthy(200L, 7_282_301_440d).Should().BeFalse();
    }

    [Fact]
    public void AnIssuerWhoseFiguresAgree_IsJudged()
    {
        ImpossiblePositionGuard
            .AnchorIsTrustworthy(NaasSharesOutstanding, NaasMarketCap)
            .Should()
            .BeTrue();
    }

    [Theory]
    [InlineData(0L, 1_000_000d)] // no share count
    [InlineData(1_000_000L, 0d)] // no market cap
    [InlineData(-5L, 1_000_000d)]
    public void AMissingFigure_LeavesTheIssuerUnjudged(
        long sharesOutstanding,
        double marketCapitalization
    )
    {
        ImpossiblePositionGuard
            .AnchorIsTrustworthy(sharesOutstanding, marketCapitalization)
            .Should()
            .BeFalse();
    }

    [Fact]
    public void AnEmptyPosition_IsNotImpossible()
    {
        ImpossiblePositionGuard
            .ExceedsTheIssuer(0L, NaasSharesOutstanding, NaasMarketCap)
            .Should()
            .BeFalse();
    }

    [Theory]
    // The plausibility band only has to reject a nonsense share count, so it is wide on purpose:
    // a sub-cent implied price and a five-figure one both still describe a real listing badly.
    [InlineData(1_000_000L, 5_000d, false)] // $0.005 a share — implied price below the floor
    [InlineData(1_000_000L, 20_000_000_000d, false)] // $20,000 a share — above the ceiling
    [InlineData(1_000_000L, 50_000_000d, true)] // $50 a share
    public void TheBandAcceptsRealListingsAndRejectsNonsense(
        long sharesOutstanding,
        double marketCapitalization,
        bool expected
    )
    {
        ImpossiblePositionGuard
            .AnchorIsTrustworthy(sharesOutstanding, marketCapitalization)
            .Should()
            .Be(expected);
    }
}

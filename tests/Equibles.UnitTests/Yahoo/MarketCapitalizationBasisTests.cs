using Equibles.Yahoo.Data.Prices;

namespace Equibles.UnitTests.Yahoo;

/// <summary>
/// The stored market cap follows the newest traded close only when the stored pair is provably a
/// statement at a price the listing traded at since its snapshot.
/// </summary>
public class MarketCapitalizationBasisTests
{
    // Happy Belly Food Group (XCNQ:HBFG) as stored on 2026-11-10: the pair was read on Saturday
    // 2026-11-07 at Friday's 1.51 close, and the page showed it beside Monday's 1.47 close.
    private const long HappyBellyShares = 166_404_266;
    private const double HappyBellyCap = 251_270_448;
    private static readonly DateOnly HappyBellySnapshot = new(2026, 11, 7);
    private static readonly TradedBar[] HappyBellyBars =
    [
        new(new DateOnly(2026, 11, 4), 1.48m, 1.56m, 1.53m),
        new(new DateOnly(2026, 11, 5), 1.49m, 1.56m, 1.56m),
        new(new DateOnly(2026, 11, 6), 1.50m, 1.55m, 1.51m),
        new(new DateOnly(2026, 11, 9), 1.43m, 1.55m, 1.47m),
    ];

    [Fact]
    public void A_cap_read_at_the_prior_close_moves_to_the_newest_close()
    {
        var restated = MarketCapitalizationBasis.OnNewestClose(
            HappyBellyShares,
            HappyBellyCap,
            quoteUnitMultiplier: null,
            HappyBellySnapshot,
            HappyBellyBars
        );

        restated.Should().BeApproximately(166_404_266 * 1.47, 0.5);
    }

    [Fact]
    public void Restating_twice_changes_nothing_and_the_next_close_still_follows()
    {
        var first = MarketCapitalizationBasis.OnNewestClose(
            HappyBellyShares,
            HappyBellyCap,
            null,
            HappyBellySnapshot,
            HappyBellyBars
        );
        var again = MarketCapitalizationBasis.OnNewestClose(
            HappyBellyShares,
            first.Value,
            null,
            HappyBellySnapshot,
            HappyBellyBars
        );
        again.Should().Be(first);

        // The snapshot stamp has not moved, yet the pair now sits on a close after it.
        TradedBar[] nextSession =
        [
            .. HappyBellyBars,
            new(new DateOnly(2026, 11, 10), 1.50m, 1.60m, 1.58m),
        ];
        MarketCapitalizationBasis
            .OnNewestClose(HappyBellyShares, first.Value, null, HappyBellySnapshot, nextSession)
            .Should()
            .BeApproximately(166_404_266 * 1.58, 0.5);
    }

    /// <summary>
    /// A pair whose cap over shares is no price the listing traded at (a depositary ratio, a
    /// parent's cap on a preferred, another currency) is left as stored.
    /// </summary>
    [Theory]
    [InlineData(166_404_266L, 3_266_515_824d)] // 13 ordinary shares per receipt
    [InlineData(166_404_266L, 25_127_044d)] // a tenth of the listing's price
    public void A_pair_that_states_no_traded_price_stands(long shares, double cap)
    {
        MarketCapitalizationBasis
            .OnNewestClose(shares, cap, null, HappyBellySnapshot, HappyBellyBars)
            .Should()
            .BeNull();
    }

    /// <summary>A price traded only before the snapshot's own window proves nothing about the stored count.</summary>
    [Fact]
    public void A_price_last_traded_before_the_snapshot_window_does_not_vouch_for_the_pair()
    {
        TradedBar[] bars =
        [
            new(new DateOnly(2026, 10, 1), 2.90m, 3.10m, 3.00m),
            new(new DateOnly(2026, 11, 6), 1.50m, 1.55m, 1.51m),
            new(new DateOnly(2026, 11, 9), 1.43m, 1.55m, 1.47m),
        ];

        MarketCapitalizationBasis
            .OnNewestClose(HappyBellyShares, 166_404_266 * 3.0, null, HappyBellySnapshot, bars)
            .Should()
            .BeNull();
    }

    /// <summary>A snapshot read during a session is at an intraday price, inside that session's range once its bar lands.</summary>
    [Fact]
    public void An_intraday_snapshot_is_vouched_for_by_its_own_sessions_bar()
    {
        var snapshot = new DateOnly(2026, 11, 9);
        var intradayCap = 166_404_266 * 1.44;

        MarketCapitalizationBasis
            .OnNewestClose(HappyBellyShares, intradayCap, null, snapshot, HappyBellyBars[..3])
            .Should()
            .BeNull("1.44 is outside Friday's range and Monday's bar is not stored yet");
        MarketCapitalizationBasis
            .OnNewestClose(HappyBellyShares, intradayCap, null, snapshot, HappyBellyBars)
            .Should()
            .BeApproximately(166_404_266 * 1.47, 0.5);
    }

    /// <summary>A London line quotes pence while its cap is in pounds.</summary>
    [Fact]
    public void A_quoted_unit_multiplier_converts_the_close_to_major_units()
    {
        TradedBar[] bars =
        [
            new(new DateOnly(2026, 11, 6), 248m, 255m, 250m),
            new(new DateOnly(2026, 11, 9), 255m, 262m, 260m),
        ];

        MarketCapitalizationBasis
            .OnNewestClose(1_000_000, 2_500_000, 0.01m, new DateOnly(2026, 11, 7), bars)
            .Should()
            .BeApproximately(2_600_000, 0.5);
    }

    [Fact]
    public void A_listing_without_bars_or_a_pair_without_a_count_stands()
    {
        MarketCapitalizationBasis
            .OnNewestClose(HappyBellyShares, HappyBellyCap, null, HappyBellySnapshot, [])
            .Should()
            .BeNull();
        MarketCapitalizationBasis
            .OnNewestClose(0, HappyBellyCap, null, HappyBellySnapshot, HappyBellyBars)
            .Should()
            .BeNull();
    }
}

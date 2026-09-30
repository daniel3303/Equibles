using Equibles.Yahoo.HostedService.Services;

namespace Equibles.UnitTests.Yahoo;

public class YahooVenueSettlementTests
{
    [Theory]
    [InlineData("CN", "XSHG", "600000", "2026-09-30T07:29:59Z", "2026-09-30")]
    [InlineData("CN", "XSHG", "600000", "2026-09-30T07:30:00Z", "2026-10-01")]
    [InlineData("CN", "XSHE", "000001", "2026-09-30T07:30:00Z", "2026-10-01")]
    [InlineData("CN", "XSHG", "600000", "2026-09-30T19:00:00Z", "2026-10-01")]
    [InlineData("HK", "XHKG", "00700", "2026-09-30T08:39:59Z", "2026-09-30")]
    [InlineData("HK", "XHKG", "00700", "2026-09-30T08:40:00Z", "2026-10-01")]
    [InlineData("GB", "XLON", "VOD", "2026-09-30T16:04:59Z", "2026-09-30")]
    [InlineData("GB", "XLON", "VOD", "2026-09-30T16:05:00Z", "2026-10-01")]
    [InlineData("GB", "XLON", "VOD", "2026-12-01T16:35:00Z", "2026-12-01")]
    [InlineData("GB", "XLON", "VOD", "2026-12-01T17:05:00Z", "2026-12-02")]
    public void Venue_clock_and_closing_auction_bound_the_daily_window(
        string country,
        string mic,
        string ticker,
        string now,
        string expected
    )
    {
        var target = Target(country, mic, ticker);
        YahooListingSource
            .SettledBefore(
                target,
                DateTime.Parse(now, null, System.Globalization.DateTimeStyles.AdjustToUniversal)
            )
            .Should()
            .Be(DateOnly.Parse(expected));
    }

    [Theory]
    [InlineData("US", "XNAS", "AAPL", false)]
    [InlineData("CN", "BJSE", "920000", false)]
    [InlineData("CN", "XSHG", "600000", true)]
    public void Us_unsupported_and_historical_targets_keep_the_utc_cutoff(
        string country,
        string mic,
        string ticker,
        bool historical
    )
    {
        var target = Target(country, mic, ticker) with { IsHistorical = historical };
        YahooListingSource
            .SettledBefore(target, new DateTime(2026, 9, 30, 23, 0, 0, DateTimeKind.Utc))
            .Should()
            .Be(new DateOnly(2026, 9, 30));
    }

    private static PriceSeriesTarget Target(string country, string mic, string ticker) =>
        new(
            ticker,
            Guid.NewGuid(),
            Guid.NewGuid(),
            false,
            MarketCountryCode: country,
            MarketIdentifierCode: mic,
            EquitySecurityId: Guid.NewGuid()
        );
}

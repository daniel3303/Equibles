using Equibles.CommonStocks.Data.Models;
using Equibles.Sec.HostedService.Services;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// Pins the pure discovery helpers: the daily-index finality boundary (a day's
/// master index keeps growing until EDGAR's evening batch, so advancing the
/// watermark too early silently drops filings) and the numeric CIK map (EDGAR
/// surfaces the same CIK zero-padded in the ATOM feed and bare in master.idx).
/// </summary>
public class FilingDiscoveryServiceStaticsTests
{
    // July dates: Eastern is EDT (UTC-4).

    [Fact]
    public void LatestFinalIndexDay_AfterSixAmEastern_IsPreviousDay()
    {
        // 11:00 UTC = 07:00 EDT on Jul 9 → Jul 8 is final.
        var result = FilingDiscoveryService.LatestFinalIndexDay(
            new DateTime(2026, 7, 9, 11, 0, 0, DateTimeKind.Utc)
        );

        result.Should().Be(new DateOnly(2026, 7, 8));
    }

    [Fact]
    public void LatestFinalIndexDay_BeforeSixAmEastern_HoldsAnExtraDay()
    {
        // 09:00 UTC = 05:00 EDT on Jul 9 → Jul 8's index may still be growing.
        var result = FilingDiscoveryService.LatestFinalIndexDay(
            new DateTime(2026, 7, 9, 9, 0, 0, DateTimeKind.Utc)
        );

        result.Should().Be(new DateOnly(2026, 7, 7));
    }

    [Fact]
    public void LatestFinalIndexDay_LateUtcEvening_IsStillSameEasternDay()
    {
        // 02:00 UTC Jul 10 = 22:00 EDT Jul 9 → Jul 8 is the latest final day.
        var result = FilingDiscoveryService.LatestFinalIndexDay(
            new DateTime(2026, 7, 10, 2, 0, 0, DateTimeKind.Utc)
        );

        result.Should().Be(new DateOnly(2026, 7, 8));
    }

    [Fact]
    public void BuildCikMap_MatchesPaddedAndBareCiks()
    {
        var company = new CommonStock { Ticker = "AAPL", Cik = "320193" };

        var map = FilingDiscoveryService.BuildCikMap([company]);

        map.Should().ContainKey(320193L).WhoseValue.Should().BeSameAs(company);
        // Padded feed CIK parses to the same key.
        long.Parse("0000320193").Should().Be(320193L);
    }

    [Fact]
    public void BuildCikMap_IncludesSecondaryCiks()
    {
        var company = new CommonStock
        {
            Ticker = "ATAI",
            Cik = "1840904",
            SecondaryCiks = ["0002012345"],
        };

        var map = FilingDiscoveryService.BuildCikMap([company]);

        map.Should().ContainKey(1840904L);
        map.Should().ContainKey(2012345L).WhoseValue.Should().BeSameAs(company);
    }

    [Fact]
    public void BuildCikMap_IgnoresUnparseableCiks()
    {
        var company = new CommonStock { Ticker = "BAD", Cik = "not-a-cik" };

        FilingDiscoveryService.BuildCikMap([company]).Should().BeEmpty();
    }
}

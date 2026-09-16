using Equibles.Integrations.Yahoo.Models;
using Equibles.Yahoo.HostedService.Configuration;
using Equibles.Yahoo.HostedService.Services;

namespace Equibles.UnitTests.Yahoo;

/// <summary>
/// Pins the catalog-market start-date rule. Uncovered listings keep today's behaviour: forward-only
/// from the Yahoo-owned latest date, widened over the resettle and heal window, no fetch once
/// current. A listing a venue keeps current (a venue bar newer than any Yahoo bar) fetches the
/// bounded recent window at most once per interval, stamped on the attempt, and still fetches from
/// the floor while it has no Yahoo-owned rows. The leading-edge gate is the only completeness check
/// on a catalog listing's first history.
/// </summary>
public class YahooPriceImportServiceCatalogStartDateTests
{
    private static readonly DateOnly Floor = new(2020, 1, 1);
    private static readonly DateOnly Today = new(2026, 9, 16);
    private static readonly DateTime Now = new(2026, 9, 16, 4, 0, 0, DateTimeKind.Utc);
    private static readonly YahooPriceScraperOptions Options = new()
    {
        VolumeResettleWindowDays = 15,
        CoveredListingFetchIntervalHours = 24,
    };

    private static DateOnly Resolve(
        DateOnly? latestYahoo,
        DateOnly? latestVenue,
        DateTime? stamp
    ) =>
        YahooPriceImportService.ResolveCatalogStartDate(
            latestYahoo,
            latestVenue,
            stamp,
            Today,
            Now,
            Floor,
            Options
        );

    [Fact]
    public void Uncovered_NoRows_StartsAtTheFloor()
    {
        Resolve(null, null, null).Should().Be(Floor);
    }

    [Fact]
    public void Uncovered_Current_NeedsNoFetch()
    {
        Resolve(Today.AddDays(-1), null, null).Should().Be(Today);
    }

    [Fact]
    public void Uncovered_Stale_WidensOverTheResettleWindow()
    {
        Resolve(Today.AddDays(-2), null, null).Should().Be(Today.AddDays(-15));
        Resolve(Today.AddDays(-40), null, null)
            .Should()
            .Be(Today.AddDays(-39), "a stale series reaches back to its own latest date");
        Resolve(Today.AddDays(-2), Today.AddDays(-3), null)
            .Should()
            .Be(Today.AddDays(-15), "a venue bar older than the feed's does not cover the listing");
    }

    [Fact]
    public void Covered_FetchesTheBoundedWindow_OncePerInterval()
    {
        var frozen = new DateOnly(2026, 8, 1);
        Resolve(frozen, Today.AddDays(-1), null)
            .Should()
            .Be(Today.AddDays(-15), "never back to the frozen latest date");
        Resolve(frozen, Today.AddDays(-1), Now.AddHours(-23))
            .Should()
            .Be(Today, "attempted inside the interval");
        Resolve(frozen, Today.AddDays(-1), Now.AddHours(-25)).Should().Be(Today.AddDays(-15));
    }

    [Fact]
    public void Covered_WithoutYahooRows_StillFetchesFromTheFloor_OncePerInterval()
    {
        Resolve(null, Today.AddDays(-1), null).Should().Be(Floor);
        Resolve(null, Today.AddDays(-1), Now.AddHours(-1)).Should().Be(Today);
    }

    [Fact]
    public void Covered_WithADefaultResettleWindow_UsesTheHealWindow()
    {
        var options = new YahooPriceScraperOptions { VolumeResettleWindowDays = 5 };
        YahooPriceImportService
            .ResolveCatalogStartDate(
                new DateOnly(2026, 8, 1),
                Today.AddDays(-1),
                null,
                Today,
                Now,
                Floor,
                options
            )
            .Should()
            .Be(Today.AddDays(-10));
    }

    private static YahooChartData Chart(DateOnly? firstTrade, params DateOnly[] dates) =>
        new()
        {
            FirstTradeDate = firstTrade,
            Prices = dates
                .Select(date => new HistoricalPrice
                {
                    Date = date,
                    Open = 1,
                    High = 1,
                    Low = 1,
                    Close = 1,
                    AdjustedClose = 1,
                    Volume = 1,
                })
                .ToList(),
        };

    [Fact]
    public void LeadingEdge_AcceptsAHistoryThatStartsNearTheFirstTradeDate()
    {
        YahooPriceImportService
            .HasCompleteLeadingEdge(
                Chart(new DateOnly(2021, 6, 1), new DateOnly(2021, 6, 7), Today.AddDays(-1)),
                Floor,
                Today
            )
            .Should()
            .BeTrue();
        YahooPriceImportService
            .HasCompleteLeadingEdge(
                Chart(new DateOnly(2019, 1, 1), new DateOnly(2020, 1, 6), Today.AddDays(-1)),
                Floor,
                Today
            )
            .Should()
            .BeTrue("the floor caps the expectation");
    }

    [Fact]
    public void LeadingEdge_RefusesAHistoryMissingItsStart()
    {
        YahooPriceImportService
            .HasCompleteLeadingEdge(
                Chart(new DateOnly(2021, 6, 1), new DateOnly(2024, 1, 2), Today.AddDays(-1)),
                Floor,
                Today
            )
            .Should()
            .BeFalse();
        YahooPriceImportService
            .HasCompleteLeadingEdge(Chart(new DateOnly(2021, 6, 1)), Floor, Today)
            .Should()
            .BeFalse("nothing storable");
        YahooPriceImportService
            .HasCompleteLeadingEdge(Chart(new DateOnly(2021, 6, 1), Today), Floor, Today)
            .Should()
            .BeFalse("the live candle is not storable");
    }

    [Fact]
    public void LeadingEdge_InstallsAnUnknownFirstTradeDate()
    {
        YahooPriceImportService
            .HasCompleteLeadingEdge(Chart(null, new DateOnly(2024, 1, 2)), Floor, Today)
            .Should()
            .BeTrue();
    }
}

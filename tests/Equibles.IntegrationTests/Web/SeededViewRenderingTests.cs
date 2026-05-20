using System.Net;
using Equibles.Cboe.Data.Models;
using Equibles.Errors.Data.Models;
using Equibles.Fred.Data.Models;
using Equibles.IntegrationTests.Helpers;
using Xunit;

namespace Equibles.IntegrationTests.Web;

/// <summary>
/// Data-driven companion to <see cref="HomeViewRenderingTests"/>. Each test seeds
/// the minimum rows a page needs, GETs it through the in-process host, and
/// asserts on HTML the view's record/stat loops are responsible for emitting —
/// so the previously-0% <c>Views_EconomicData_Show</c>, <c>Views_Market_Vix</c>
/// and <c>Views_Status_Index</c> compiled views are exercised along their
/// populated paths, not just their empty-state branches.
/// </summary>
[Collection(WebHostCollection.Name)]
public class SeededViewRenderingTests
{
    private readonly WebHostFixture _fixture;

    public SeededViewRenderingTests(WebHostFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task GetEconomicDataShow_WithSeededSeriesAndObservations_RendersStatsAndTable()
    {
        await _fixture.ResetAndSeedAsync(async db =>
        {
            var series = new FredSeries
            {
                SeriesId = "DGS10",
                Title = "10-Year Treasury Constant Maturity Rate",
                Category = FredSeriesCategory.InterestRates,
                Frequency = "D",
                Units = "Percent",
                SeasonalAdjustment = "Not Seasonally Adjusted",
            };
            db.Add(series);
            db.AddRange(
                new FredObservation
                {
                    FredSeries = series,
                    Date = new DateOnly(2026, 1, 2),
                    Value = 4.10m,
                },
                new FredObservation
                {
                    FredSeries = series,
                    Date = new DateOnly(2026, 1, 3),
                    Value = 4.25m,
                },
                new FredObservation
                {
                    FredSeries = series,
                    Date = new DateOnly(2026, 1, 6),
                    Value = 4.18m,
                }
            );
            await Task.CompletedTask;
        });

        var response = await _fixture.Client.GetAsync("/EconomicData/DGS10");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should()
            .Contain("10-Year Treasury Constant Maturity Rate", "the series title must render");
        html.Should().Contain("DGS10");
    }

    [Fact]
    public async Task GetMarketVix_WithSeededDailyRows_RendersRecordsAndStatistics()
    {
        await _fixture.ResetAndSeedAsync(async db =>
        {
            db.AddRange(
                new CboeVixDaily
                {
                    Date = new DateOnly(2026, 1, 5),
                    Open = 13.1m,
                    High = 14.0m,
                    Low = 12.9m,
                    Close = 13.8m,
                },
                new CboeVixDaily
                {
                    Date = new DateOnly(2026, 1, 6),
                    Open = 13.8m,
                    High = 15.2m,
                    Low = 13.5m,
                    Close = 14.9m,
                }
            );
            await Task.CompletedTask;
        });

        var response = await _fixture.Client.GetAsync("/Market/Vix");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("VIX", "the VIX view title must render");
    }

    [Fact]
    public async Task GetStatusIndex_WithSeededError_RendersRecentErrorsTable()
    {
        await _fixture.ResetAndSeedAsync(async db =>
        {
            db.Add(
                new Error
                {
                    Source = ErrorSource.HoldingsScraper,
                    Context = "Holdings.ProcessDataSet",
                    Message = "Seeded test error for view rendering",
                    CreationTime = DateTime.UtcNow,
                }
            );
            await Task.CompletedTask;
        });

        var response = await _fixture.Client.GetAsync("/Status");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should()
            .Contain(
                "Seeded test error for view rendering",
                "the seeded error must appear in the recent-errors table"
            );
        // LowercaseUrls = true in Program.cs forces the rendered href to /status/activity.
        html.Should()
            .Contain(
                "/status/activity",
                "the Live activity link must point at the new page so users can navigate to it"
            );
    }

    [Fact]
    public async Task GetStatusActivity_RendersLiveActivityPageWithStreamUrl()
    {
        var response = await _fixture.Client.GetAsync("/Status/Activity");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("Live activity", "the page heading must render");
        html.Should()
            .Contain(
                "/status/activity/stream",
                "the JS must point at the SSE endpoint resolved through Url.Action"
            );
        html.Should()
            .Contain(
                "data-activity-list",
                "the live-tail list must be present for the script to fill in"
            );
    }
}

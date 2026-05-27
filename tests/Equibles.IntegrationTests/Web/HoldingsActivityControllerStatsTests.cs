using System.Net;
using Equibles.Holdings.Data.Models;
using Equibles.IntegrationTests.Helpers;
using Xunit;

namespace Equibles.IntegrationTests.Web;

[Collection(WebHostCollection.Name)]
public class HoldingsActivityControllerStatsTests
{
    private readonly WebHostFixture _fixture;

    public HoldingsActivityControllerStatsTests(WebHostFixture fixture) => _fixture = fixture;

    // The stats dashboard reads from the per-quarter snapshot table that the
    // worker rebuilds on each 13F import (with a daily safety-net pass). This
    // test pins that the route resolves, the controller reads the snapshot
    // row, and the view renders the quarterly history table + summary cards.
    // Aggregate-correctness (distinct filer/stock/filing counts) is covered
    // by HoldingsAggregateRefreshServiceTests at the integration tier.
    [Fact]
    public async Task Stats_WithSeededSnapshot_RendersStatsTableAndSummaryCards()
    {
        var reportDate = new DateOnly(2024, 12, 31);

        await _fixture.ResetAndSeedAsync(async db =>
        {
            db.Add(
                new AumQuarterlySnapshot
                {
                    ReportDate = reportDate,
                    TotalValue = 2_000_000,
                    FilerCount = 1,
                    PositionCount = 1,
                    StockCount = 1,
                    FilingCount = 1,
                }
            );
            await Task.CompletedTask;
        });

        var response = await _fixture.Client.GetAsync("/holdings/stats");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("data-testid=\"stats-table\"");
        html.Should().Contain("13F Statistics");
        html.Should().Contain("Latest Quarter");
    }
}

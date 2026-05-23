using System.Net;
using Equibles.CommonStocks.Data.Models;
using Equibles.Holdings.Data.Models;
using Equibles.IntegrationTests.Helpers;
using Xunit;

namespace Equibles.IntegrationTests.Web;

[Collection(WebHostCollection.Name)]
public class HoldingsActivityControllerStatsTests
{
    private readonly WebHostFixture _fixture;

    public HoldingsActivityControllerStatsTests(WebHostFixture fixture) => _fixture = fixture;

    // The stats dashboard (#1927) has zero integration coverage. This test
    // pins that the route resolves, the view renders the quarterly history
    // table with seeded data, and the summary cards display when at least
    // one quarter of data exists — exercising routing → controller →
    // GetAumByReportDate aggregate query → Razor view.
    [Fact]
    public async Task Stats_WithSeededHoldings_RendersStatsTableAndSummaryCards()
    {
        var stockId = Guid.NewGuid();
        var holderId = Guid.NewGuid();
        var reportDate = new DateOnly(2024, 12, 31);

        await _fixture.ResetAndSeedAsync(async db =>
        {
            db.Add(
                new CommonStock
                {
                    Id = stockId,
                    Ticker = "AAPL",
                    Name = "Apple Inc.",
                    Cik = "0000320193",
                }
            );
            db.Add(
                new InstitutionalHolder
                {
                    Id = holderId,
                    Cik = "0004000001",
                    Name = "Stats Test Fund LP",
                }
            );
            db.Add(
                new InstitutionalHolding
                {
                    CommonStockId = stockId,
                    InstitutionalHolderId = holderId,
                    ReportDate = reportDate,
                    FilingDate = reportDate.AddDays(45),
                    Shares = 10_000,
                    Value = 2_000_000,
                    ShareType = ShareType.Shares,
                    InvestmentDiscretion = InvestmentDiscretion.Sole,
                    AccessionNumber = "acc-stats-test1",
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

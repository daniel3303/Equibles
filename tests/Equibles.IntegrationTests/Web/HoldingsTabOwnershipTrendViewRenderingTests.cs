using System.Net;
using Equibles.CommonStocks.Data.Models;
using Equibles.Holdings.Data.Models;
using Equibles.IntegrationTests.Helpers;
using Xunit;

namespace Equibles.IntegrationTests.Web;

/// <summary>
/// End-to-end coverage of the holdings tab's Institutional Ownership Trend chart
/// through the real Web host: with two seeded quarters the chart card and its
/// per-quarter shares/holder-count series render; with a single quarter the
/// chart is omitted (one point cannot draw a trend line).
/// </summary>
[Collection(WebHostCollection.Name)]
public class HoldingsTabOwnershipTrendViewRenderingTests
{
    private readonly WebHostFixture _fixture;

    public HoldingsTabOwnershipTrendViewRenderingTests(WebHostFixture fixture) =>
        _fixture = fixture;

    [Fact]
    public async Task GetHoldings_TwoQuarters_RendersOwnershipTrendChartWithSeries()
    {
        var stockId = Guid.NewGuid();
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
            var holder = new InstitutionalHolder { Cik = "H0000001", Name = "Test Holder" };
            db.Add(holder);
            db.Add(MakeHolding(stockId, holder, new DateOnly(2024, 9, 30), 123_456));
            db.Add(MakeHolding(stockId, holder, new DateOnly(2024, 12, 31), 654_321));
            await Task.CompletedTask;
        });

        var response = await _fixture.Client.GetAsync("/Stocks/AAPL/Holdings");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("Institutional Ownership Trend", "the chart card renders");
        html.Should().Contain("ownership-trend-chart", "the chart canvas renders");
        html.Should().Contain("2024-09-30", "the prior quarter is a chart label");
        html.Should().Contain("123456", "the prior quarter's total shares feed the series");
        html.Should().Contain("654321", "the current quarter's total shares feed the series");
    }

    [Fact]
    public async Task GetHoldings_SingleQuarter_OmitsOwnershipTrendChart()
    {
        var stockId = Guid.NewGuid();
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
            var holder = new InstitutionalHolder { Cik = "H0000001", Name = "Test Holder" };
            db.Add(holder);
            db.Add(MakeHolding(stockId, holder, new DateOnly(2024, 12, 31), 1_000));
            await Task.CompletedTask;
        });

        var response = await _fixture.Client.GetAsync("/Stocks/AAPL/Holdings");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().NotContain("ownership-trend-chart", "one quarter cannot draw a trend line");
    }

    private static InstitutionalHolding MakeHolding(
        Guid stockId,
        InstitutionalHolder holder,
        DateOnly reportDate,
        long shares
    ) =>
        new()
        {
            CommonStockId = stockId,
            InstitutionalHolder = holder,
            ReportDate = reportDate,
            FilingDate = reportDate.AddDays(45),
            Shares = shares,
            Value = shares * 10,
            ShareType = ShareType.Shares,
            InvestmentDiscretion = InvestmentDiscretion.Sole,
            AccessionNumber = $"acc-{reportDate:yyyyMMdd}-0001",
        };
}

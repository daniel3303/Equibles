using System.Net;
using Equibles.CommonStocks.Data.Models;
using Equibles.Data;
using Equibles.Finra.Data.Models;
using Equibles.IntegrationTests.Helpers;

namespace Equibles.IntegrationTests.Web;

/// <summary>
/// Drives the /short-volume Razor view end-to-end: the ranked row renders the
/// ticker as a resolved stock link, the company name, the short-volume
/// percentage, and the trading-day selector reflects the selected day.
/// </summary>
[Collection(WebHostCollection.Name)]
public class ShortVolumeIndexViewRenderingTests
{
    private readonly WebHostFixture _fixture;

    public ShortVolumeIndexViewRenderingTests(WebHostFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task GetIndex_WithData_RendersRowAndDaySelector()
    {
        var day = new DateOnly(2026, 5, 20);
        await _fixture.ResetAndSeedAsync(async db =>
        {
            var stockId = Guid.NewGuid();
            db.Add(
                new CommonStock
                {
                    Id = stockId,
                    Ticker = "AAPL",
                    Name = "Apple Inc.",
                }
            );
            db.Add(
                new DailyShortVolume
                {
                    CommonStockId = stockId,
                    Date = day,
                    ShortVolume = 600,
                    ShortExemptVolume = 25,
                    TotalVolume = 1_000,
                    Market = "FNYX",
                }
            );
            await Task.CompletedTask;
        });

        var response = await _fixture.Client.GetAsync("/short-volume");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        // Ticker resolves to the per-stock page via the shared _StockLink partial.
        html.Should().Contain("/stocks/aapl");
        html.Should().Contain("Apple Inc.");
        // 600 / 1000 → 60.0% short of total volume.
        html.Should().Contain("60.0%");
        // The day selector carries the selected trading day option.
        html.Should().Contain("value=\"2026-05-20\"");
        html.Should().Contain("May 20, 2026");
    }
}

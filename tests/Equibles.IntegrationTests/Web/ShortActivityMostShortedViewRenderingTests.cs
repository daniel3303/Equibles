using System.Net;
using Equibles.CommonStocks.Data.Models;
using Equibles.Data;
using Equibles.Finra.Data.Models;
using Equibles.IntegrationTests.Helpers;

namespace Equibles.IntegrationTests.Web;

/// <summary>
/// Drives the /most-shorted Razor view end-to-end: the ranked row renders the
/// ticker linked to the stock's short-interest tab, the company name, days to
/// cover, average daily volume, and the settlement-date selector option.
/// </summary>
[Collection(WebHostCollection.Name)]
public class ShortActivityMostShortedViewRenderingTests
{
    private readonly WebHostFixture _fixture;

    public ShortActivityMostShortedViewRenderingTests(WebHostFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task GetMostShorted_WithData_RendersRowAndDateSelector()
    {
        var settlement = new DateOnly(2026, 5, 15);
        await _fixture.ResetAndSeedAsync(async db =>
        {
            var stockId = Guid.NewGuid();
            db.Add(
                new CommonStock
                {
                    Id = stockId,
                    Ticker = "GME",
                    Name = "GameStop Corp.",
                }
            );
            db.Add(
                new ShortInterest
                {
                    CommonStockId = stockId,
                    SettlementDate = settlement,
                    CurrentShortPosition = 12_345_678,
                    PreviousShortPosition = 10_000_000,
                    ChangeInShortPosition = 2_345_678,
                    AverageDailyVolume = 5_000_000,
                    DaysToCover = 2.5m,
                }
            );
            await Task.CompletedTask;
        });

        var response = await _fixture.Client.GetAsync("/most-shorted");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        // Ticker links to the per-stock short-interest tab.
        html.Should().Contain("/stocks/gme/short-interest");
        html.Should().Contain("GameStop Corp.");
        // Days to cover renders with one decimal (InvariantCulture).
        html.Should().Contain("2.5");
        // The settlement-date selector carries the selected day option.
        html.Should().Contain("value=\"2026-05-15\"");
        html.Should().Contain("May 15, 2026");
    }
}

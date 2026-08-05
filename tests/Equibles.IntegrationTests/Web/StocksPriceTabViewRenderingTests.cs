using System.Net;
using Equibles.CommonStocks.Data.Models;
using Equibles.Data;
using Equibles.IntegrationTests.Helpers;
using Equibles.Yahoo.Data.Models;
using Xunit;

namespace Equibles.IntegrationTests.Web;

/// <summary>
/// Drives the real Razor pipeline for <c>Views/Stocks/_PriceTab.cshtml</c> to pin
/// the Performance panel (returns vs SPY) added for the price-performance feature.
/// Controller/tab-service tests instantiate types directly and never render the
/// view, so the panel markup is otherwise uncovered.
/// </summary>
[Collection(WebHostCollection.Name)]
public class StocksPriceTabViewRenderingTests
{
    private readonly WebHostFixture _fixture;

    public StocksPriceTabViewRenderingTests(WebHostFixture fixture) => _fixture = fixture;

    private static void AddDailyPrices(
        EquiblesFinancialDbContext db,
        Guid stockId,
        DateOnly startDate,
        decimal[] closes
    )
    {
        for (var i = 0; i < closes.Length; i++)
        {
            db.Add(
                new DailyStockPrice
                {
                    CommonStockId = stockId,
                    Date = startDate.AddDays(i),
                    Open = closes[i],
                    High = closes[i],
                    Low = closes[i],
                    Close = closes[i],
                    AdjustedClose = closes[i],
                    Volume = 1_000_000,
                }
            );
        }
    }

    [Fact]
    public async Task GetPriceTab_WithStockAndBenchmarkPrices_RendersPerformancePanelVsSpy()
    {
        var aaplId = Guid.NewGuid();
        var spyId = Guid.NewGuid();

        await _fixture.ResetAndSeedAsync(async db =>
        {
            db.Add(
                new CommonStock
                {
                    Id = aaplId,
                    Ticker = "AAPL",
                    Name = "Apple Inc.",
                }
            );
            db.Add(
                new CommonStock
                {
                    Id = spyId,
                    Ticker = "SPY",
                    Name = "SPDR S&P 500 ETF",
                }
            );
            var start = new DateOnly(2025, 6, 2);
            // 6 bars each → the 5-day window is in range for both series, so the
            // benchmark and "vs SPY" rows render with real numbers.
            AddDailyPrices(db, aaplId, start, [100m, 102m, 104m, 106m, 108m, 110m]);
            AddDailyPrices(db, spyId, start, [100m, 101m, 102m, 103m, 104m, 105m]);
            await Task.CompletedTask;
        });

        var response = await _fixture.Client.GetAsync("/Stocks/AAPL/price");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("Performance", "the Performance panel heading must render");
        html.Should().Contain("vs SPY", "the benchmark-comparison row must render");
        // Razor's HTML encoder emits the leading "+" as &#x2B; (renders as "+" in the
        // browser); the percentage uses a dot via InvariantCulture regardless of host.
        html.Should().Contain("10.0%", "AAPL's 5-day return must render");
        html.Should().Contain("5.0%", "SPY's 5-day return must render");
        html.Should().Contain("text-success", "gains must be colored as successes");
    }

    [Fact]
    public async Task GetPriceTab_NoBenchmarkTracked_RendersPanelWithUnavailableNote()
    {
        var aaplId = Guid.NewGuid();

        await _fixture.ResetAndSeedAsync(async db =>
        {
            db.Add(
                new CommonStock
                {
                    Id = aaplId,
                    Ticker = "AAPL",
                    Name = "Apple Inc.",
                }
            );
            AddDailyPrices(
                db,
                aaplId,
                new DateOnly(2025, 6, 2),
                [100m, 102m, 104m, 106m, 108m, 110m]
            );
            await Task.CompletedTask;
        });

        var response = await _fixture.Client.GetAsync("/Stocks/AAPL/price");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("Performance");
        html.Should()
            .Contain(
                "SPY comparison unavailable",
                "with no SPY tracked the fallback note must render"
            );
    }

    [Theory]
    [InlineData("/Stocks/BRK-A/price")]
    [InlineData("/Stocks/BRK.A/price")]
    public async Task GetPriceTab_SecondaryListingAndDotAlias_RenderExactSecondarySeries(
        string path
    )
    {
        var stockId = Guid.NewGuid();

        await _fixture.ResetAndSeedAsync(async db =>
        {
            db.Add(
                new CommonStock
                {
                    Id = stockId,
                    Ticker = "BRK-B",
                    SecondaryTickers = ["BRK-A"],
                    Name = "Berkshire Hathaway Inc.",
                }
            );
            var date = new DateOnly(2026, 8, 3);
            db.AddRange(
                new DailyStockPrice
                {
                    CommonStockId = stockId,
                    ListedTicker = "BRK-B",
                    Date = date,
                    Open = 299m,
                    High = 301m,
                    Low = 298m,
                    Close = 300m,
                    AdjustedClose = 300m,
                    Volume = 1_000,
                },
                new DailyStockPrice
                {
                    CommonStockId = stockId,
                    ListedTicker = "BRK-A",
                    Date = date,
                    Open = 599_000m,
                    High = 601_000m,
                    Low = 598_000m,
                    Close = 600_000m,
                    AdjustedClose = 600_000m,
                    Volume = 100,
                }
            );
            await Task.CompletedTask;
        });

        var response = await _fixture.Client.GetAsync(path);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain(">BRK-A</h1>");
        html.Should().Contain("const allClose = [600000.0000];");
        html.Should().NotContain("const allClose = [300.0000];");
        html.Should().Contain("/stocks/brk-a/holdings");
    }
}

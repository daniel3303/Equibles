using System.Net;
using Equibles.Cboe.Data.Models;
using Equibles.CommonStocks.Data.Models;
using Equibles.IntegrationTests.Helpers;
using Equibles.Yahoo.Data.Models;
using Xunit;

namespace Equibles.IntegrationTests.Web;

/// <summary>
/// Continues the in-process Razor coverage seam: the CBOE put/call ratio page
/// and the Stocks "Show" shell (reached via the Price tab) were both 0%. Each
/// test seeds the minimum rows, GETs the route, and asserts on HTML the view
/// emits so the compiled <c>Views_Market_PutCallRatio</c> and
/// <c>Views_Stocks_Show</c> classes are exercised end-to-end.
/// </summary>
[Collection(WebHostCollection.Name)]
public class PutCallAndStockViewRenderingTests
{
    private readonly WebHostFixture _fixture;

    public PutCallAndStockViewRenderingTests(WebHostFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task GetMarketPutCallRatio_WithSeededRows_RendersRecordsAndStatistics()
    {
        await _fixture.ResetAndSeedAsync(async db =>
        {
            db.AddRange(
                new CboePutCallRatio
                {
                    RatioType = CboePutCallRatioType.Equity,
                    Date = new DateOnly(2026, 1, 5),
                    CallVolume = 1_200_000,
                    PutVolume = 780_000,
                    TotalVolume = 1_980_000,
                    PutCallRatio = 0.65m,
                },
                new CboePutCallRatio
                {
                    RatioType = CboePutCallRatioType.Equity,
                    Date = new DateOnly(2026, 1, 6),
                    CallVolume = 1_100_000,
                    PutVolume = 880_000,
                    TotalVolume = 1_980_000,
                    PutCallRatio = 0.80m,
                }
            );
            await Task.CompletedTask;
        });

        var response = await _fixture.Client.GetAsync("/Market/PutCallRatio/Equity");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("Equity", "the put/call ratio display name must render");
    }

    [Fact]
    public async Task GetStocksPrice_WithSeededStock_RendersShowShell()
    {
        await _fixture.ResetAndSeedAsync(async db =>
        {
            db.Add(
                new CommonStock
                {
                    Cik = "0000320193",
                    Ticker = "AAPL",
                    Name = "Apple Inc.",
                    Description = "Seeded for view-rendering coverage",
                }
            );
            await Task.CompletedTask;
        });

        var response = await _fixture.Client.GetAsync("/Stocks/AAPL/Price");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("AAPL", "the Show shell must render the stock ticker");
        html.Should().Contain("Apple Inc.", "the Show shell must render the stock name");
    }

    [Fact]
    public async Task GetStocksPrice_WithRisingPriceStreak_RendersUpStreakBadge()
    {
        var stockId = Guid.NewGuid();
        await _fixture.ResetAndSeedAsync(async db =>
        {
            db.Add(
                new CommonStock
                {
                    Id = stockId,
                    Cik = "0000789019",
                    Ticker = "MSFT",
                    Name = "Microsoft Corporation",
                }
            );

            // 40 trading days of strictly rising closes — every day closed higher
            // than the prior, so the Price tab must surface a consecutive up-day
            // streak badge.
            var start = new DateOnly(2026, 1, 5);
            for (var i = 0; i < 40; i++)
            {
                var close = 100m + i;
                db.Add(
                    new DailyStockPrice
                    {
                        CommonStockId = stockId,
                        Date = start.AddDays(i),
                        Open = close,
                        High = close,
                        Low = close,
                        Close = close,
                        AdjustedClose = close,
                        Volume = 1_000_000,
                    }
                );
            }
            await Task.CompletedTask;
        });

        var response = await _fixture.Client.GetAsync("/Stocks/MSFT/Price");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("Technical signals", "the technical-signal badge row must render");
        html.Should()
            .Contain(
                "Up Days",
                "a rising price series must surface a consecutive up-day streak badge"
            );
    }
}

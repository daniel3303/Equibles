using System.Net;
using Equibles.CommonStocks.Data.Models;
using Equibles.Data;
using Equibles.Finra.Data.Models;
using Equibles.IntegrationTests.Helpers;

namespace Equibles.IntegrationTests.Web;

/// <summary>
/// Pins the /most-shorted market-wide leaderboard: for a settlement date (default
/// latest) the controller ranks stocks by current short position and the rendered
/// HTML carries the listing in the right order, honours the sort and settlement-date
/// selectors, and links each row to the stock's short-interest tab.
/// </summary>
[Collection(WebHostCollection.Name)]
public class ShortActivityControllerTests
{
    private readonly WebHostFixture _fixture;

    public ShortActivityControllerTests(WebHostFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task GetMostShorted_NoData_RendersEmptyState()
    {
        await _fixture.ResetAndSeedAsync(_ => Task.CompletedTask);

        var response = await _fixture.Client.GetAsync("/most-shorted");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("No short interest");
        html.Should().Contain("most-shorted-table");
    }

    [Fact]
    public async Task GetMostShorted_DefaultSort_RanksByCurrentShortPositionDescending()
    {
        var settlement = new DateOnly(2026, 5, 15);
        await _fixture.ResetAndSeedAsync(async db =>
        {
            SeedShortInterest(
                db,
                "HIGH",
                "High Short Co.",
                settlement,
                currentShort: 9_000_000,
                daysToCover: 5.0m
            );
            SeedShortInterest(
                db,
                "MID",
                "Mid Short Co.",
                settlement,
                currentShort: 5_000_000,
                daysToCover: 3.0m
            );
            SeedShortInterest(
                db,
                "LOW",
                "Low Short Co.",
                settlement,
                currentShort: 1_000_000,
                daysToCover: 1.0m
            );
            await Task.CompletedTask;
        });

        var response = await _fixture.Client.GetAsync("/most-shorted");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        var highIdx = html.IndexOf(">HIGH<", StringComparison.Ordinal);
        var midIdx = html.IndexOf(">MID<", StringComparison.Ordinal);
        var lowIdx = html.IndexOf(">LOW<", StringComparison.Ordinal);
        highIdx.Should().BeGreaterThan(-1);
        midIdx.Should().BeGreaterThan(highIdx);
        lowIdx.Should().BeGreaterThan(midIdx);
    }

    [Fact]
    public async Task GetMostShorted_SortByDaysToCover_OrdersByDaysToCoverDescending()
    {
        var settlement = new DateOnly(2026, 5, 15);
        await _fixture.ResetAndSeedAsync(async db =>
        {
            // BIG has the largest short position but a tiny days-to-cover; SQUEEZE inverts that.
            SeedShortInterest(
                db,
                "BIG",
                "Big Co.",
                settlement,
                currentShort: 9_000_000,
                daysToCover: 1.0m
            );
            SeedShortInterest(
                db,
                "SQUEEZE",
                "Squeeze Co.",
                settlement,
                currentShort: 1_000_000,
                daysToCover: 12.0m
            );
            await Task.CompletedTask;
        });

        var response = await _fixture.Client.GetAsync("/most-shorted?sort=DaysToCoverDescending");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        var squeezeIdx = html.IndexOf(">SQUEEZE<", StringComparison.Ordinal);
        var bigIdx = html.IndexOf(">BIG<", StringComparison.Ordinal);
        squeezeIdx.Should().BeGreaterThan(-1);
        bigIdx.Should().BeGreaterThan(squeezeIdx);
    }

    [Fact]
    public async Task GetMostShorted_DateParam_SelectsThatSettlementDate()
    {
        var latest = new DateOnly(2026, 5, 29);
        var older = new DateOnly(2026, 5, 15);
        await _fixture.ResetAndSeedAsync(async db =>
        {
            SeedShortInterest(
                db,
                "NEW",
                "Newdate Co.",
                latest,
                currentShort: 8_000_000,
                daysToCover: 4.0m
            );
            SeedShortInterest(
                db,
                "OLD",
                "Olddate Co.",
                older,
                currentShort: 8_000_000,
                daysToCover: 4.0m
            );
            await Task.CompletedTask;
        });

        var latestHtml = await (
            await _fixture.Client.GetAsync("/most-shorted")
        ).Content.ReadAsStringAsync();
        latestHtml.Should().Contain(">NEW<");
        latestHtml.Should().NotContain(">OLD<");

        var olderHtml = await (
            await _fixture.Client.GetAsync("/most-shorted?date=2026-05-15")
        ).Content.ReadAsStringAsync();
        olderHtml.Should().Contain(">OLD<");
        olderHtml.Should().NotContain(">NEW<");
    }

    private static void SeedShortInterest(
        EquiblesFinancialDbContext db,
        string ticker,
        string name,
        DateOnly settlementDate,
        long currentShort,
        decimal daysToCover
    )
    {
        var stockId = Guid.NewGuid();
        db.Add(
            new CommonStock
            {
                Id = stockId,
                Ticker = ticker,
                Name = name,
            }
        );
        db.Add(
            new ShortInterest
            {
                CommonStockId = stockId,
                SettlementDate = settlementDate,
                CurrentShortPosition = currentShort,
                PreviousShortPosition = currentShort / 2,
                ChangeInShortPosition = currentShort - currentShort / 2,
                AverageDailyVolume = 1_000_000,
                DaysToCover = daysToCover,
            }
        );
    }
}

using System.Net;
using Equibles.CommonStocks.Data.Models;
using Equibles.Data;
using Equibles.Finra.Data.Models;
using Equibles.IntegrationTests.Helpers;

namespace Equibles.IntegrationTests.Web;

/// <summary>
/// Pins the /short-volume market-wide ranking page: for a trading day (default
/// latest) the controller ranks stocks by daily short volume and the rendered
/// HTML carries the listing in the right order, honours the sort and trading-day
/// selectors, and excludes zero-total-volume rows (matching the
/// GetLargestShortVolume MCP tool).
/// </summary>
[Collection(WebHostCollection.Name)]
public class ShortVolumeControllerTests
{
    private readonly WebHostFixture _fixture;

    public ShortVolumeControllerTests(WebHostFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task GetIndex_NoData_RendersEmptyState()
    {
        await _fixture.ResetAndSeedAsync(_ => Task.CompletedTask);

        var response = await _fixture.Client.GetAsync("/short-volume");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("No daily short volume");
        html.Should().Contain("short-volume-table");
    }

    [Fact]
    public async Task GetIndex_DefaultSort_RanksByShortVolumeDescending()
    {
        var day = new DateOnly(2026, 5, 20);
        await _fixture.ResetAndSeedAsync(async db =>
        {
            SeedStockWithVolume(
                db,
                "HIGH",
                "High Short Co.",
                day,
                shortVolume: 900,
                totalVolume: 1_000
            );
            SeedStockWithVolume(
                db,
                "MID",
                "Mid Short Co.",
                day,
                shortVolume: 500,
                totalVolume: 1_000
            );
            SeedStockWithVolume(
                db,
                "LOW",
                "Low Short Co.",
                day,
                shortVolume: 100,
                totalVolume: 1_000
            );
            await Task.CompletedTask;
        });

        var response = await _fixture.Client.GetAsync("/short-volume");

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
    public async Task GetIndex_SortByTicker_OrdersAlphabetically()
    {
        var day = new DateOnly(2026, 5, 20);
        await _fixture.ResetAndSeedAsync(async db =>
        {
            SeedStockWithVolume(db, "ZZZ", "Zeta Co.", day, shortVolume: 900, totalVolume: 1_000);
            SeedStockWithVolume(db, "AAA", "Alpha Co.", day, shortVolume: 100, totalVolume: 1_000);
            await Task.CompletedTask;
        });

        var response = await _fixture.Client.GetAsync("/short-volume?sort=Ticker");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        // AAA (lowest short volume) sorts first by ticker despite ranking last by volume.
        var aaaIdx = html.IndexOf(">AAA<", StringComparison.Ordinal);
        var zzzIdx = html.IndexOf(">ZZZ<", StringComparison.Ordinal);
        aaaIdx.Should().BeGreaterThan(-1);
        zzzIdx.Should().BeGreaterThan(aaaIdx);
    }

    [Fact]
    public async Task GetIndex_DateParam_SelectsThatTradingDay()
    {
        var latest = new DateOnly(2026, 5, 20);
        var older = new DateOnly(2026, 5, 13);
        await _fixture.ResetAndSeedAsync(async db =>
        {
            SeedStockWithVolume(
                db,
                "NEW",
                "Newday Co.",
                latest,
                shortVolume: 800,
                totalVolume: 1_000
            );
            SeedStockWithVolume(
                db,
                "OLD",
                "Oldday Co.",
                older,
                shortVolume: 800,
                totalVolume: 1_000
            );
            await Task.CompletedTask;
        });

        // Default request resolves to the latest day.
        var latestHtml = await (
            await _fixture.Client.GetAsync("/short-volume")
        ).Content.ReadAsStringAsync();
        latestHtml.Should().Contain(">NEW<");
        latestHtml.Should().NotContain(">OLD<");

        // Explicit older day swaps the listing.
        var olderHtml = await (
            await _fixture.Client.GetAsync("/short-volume?date=2026-05-13")
        ).Content.ReadAsStringAsync();
        olderHtml.Should().Contain(">OLD<");
        olderHtml.Should().NotContain(">NEW<");
    }

    [Fact]
    public async Task GetIndex_ExcludesZeroTotalVolumeRows()
    {
        var day = new DateOnly(2026, 5, 20);
        await _fixture.ResetAndSeedAsync(async db =>
        {
            SeedStockWithVolume(db, "REAL", "Real Co.", day, shortVolume: 400, totalVolume: 1_000);
            SeedStockWithVolume(db, "ZERO", "Zero Co.", day, shortVolume: 0, totalVolume: 0);
            await Task.CompletedTask;
        });

        var response = await _fixture.Client.GetAsync("/short-volume");

        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain(">REAL<");
        html.Should().NotContain(">ZERO<");
    }

    private static void SeedStockWithVolume(
        EquiblesFinancialDbContext db,
        string ticker,
        string name,
        DateOnly date,
        long shortVolume,
        long totalVolume
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
            new DailyShortVolume
            {
                CommonStockId = stockId,
                Date = date,
                ShortVolume = shortVolume,
                ShortExemptVolume = 0,
                TotalVolume = totalVolume,
                Market = "FNYX",
            }
        );
    }
}

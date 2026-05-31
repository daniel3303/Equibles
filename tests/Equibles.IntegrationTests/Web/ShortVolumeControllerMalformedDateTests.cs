using System.Net;
using Equibles.CommonStocks.Data.Models;
using Equibles.Data;
using Equibles.Finra.Data.Models;
using Equibles.IntegrationTests.Helpers;

namespace Equibles.IntegrationTests.Web;

/// <summary>
/// Adversarial: the /short-volume day selector posts untrusted query text. The
/// controller's contract is that "an unparseable or missing value falls back to
/// the latest available trading day" — it must never let a malformed date reach
/// the query and surface as an error. The existing ShortVolumeController tests
/// only feed a missing or well-formed date; the hostile-input arm is unpinned.
/// </summary>
[Collection(WebHostCollection.Name)]
public class ShortVolumeControllerMalformedDateTests
{
    private readonly WebHostFixture _fixture;

    public ShortVolumeControllerMalformedDateTests(WebHostFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task GetIndex_MalformedDateParam_FallsBackToLatestTradingDayAndStaysOk()
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

        // Garbage that DateOnly.TryParseExact("yyyy-MM-dd") rejects, including a
        // calendar-impossible date — neither may throw nor select a stale day.
        var response = await _fixture.Client.GetAsync("/short-volume?date=2026-13-99");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain(">NEW<");
        html.Should().NotContain(">OLD<");
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

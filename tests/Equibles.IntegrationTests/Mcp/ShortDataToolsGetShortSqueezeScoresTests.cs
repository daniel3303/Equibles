using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.CorporateActions.Repositories;
using Equibles.Finra.BusinessLogic;
using Equibles.Finra.Data.Models;
using Equibles.Finra.Mcp.Tools;
using Equibles.Finra.Repositories;
using Equibles.IntegrationTests.Helpers;
using FluentAssertions;
using Xunit;

namespace Equibles.IntegrationTests.Mcp;

[Collection(ParadeDbCollection.Name)]
public class ShortDataToolsGetShortSqueezeScoresTests : ParadeDbMcpTestBase
{
    private ShortDataTools Sut() =>
        new(
            new DailyShortVolumeRepository(DbContext),
            new ShortInterestRepository(DbContext),
            new CommonStockRepository(DbContext),
            new ShortSqueezeScoreManager(
                new ShortInterestRepository(DbContext),
                new DailyShortVolumeRepository(DbContext),
                new CommonStockRepository(DbContext),
                new StockSplitRepository(DbContext)
            ),
            new StockSplitRepository(DbContext),
            ErrorManager,
            NullLogger<ShortDataTools>()
        );

    public ShortDataToolsGetShortSqueezeScoresTests(ParadeDbFixture fixture)
        : base(fixture) { }

    // The tool renders the scored universe highest-composite first, anchored to the
    // latest settlement date, and says so plainly when nothing is scored.
    [Fact]
    public async Task GetShortSqueezeScores_RanksScoredStocksHighestFirst()
    {
        var settlement = new DateOnly(2026, 4, 15);
        var hot = new CommonStock
        {
            Ticker = "HOT",
            Name = "Hot Corp",
            Cik = "0000000101",
            SharesOutStanding = 1_000_000,
        };
        var cold = new CommonStock
        {
            Ticker = "COLD",
            Name = "Cold Corp",
            Cik = "0000000102",
            SharesOutStanding = 1_000_000,
        };
        DbContext.Set<CommonStock>().AddRange(hot, cold);
        DbContext
            .Set<ShortInterest>()
            .AddRange(
                new ShortInterest
                {
                    CommonStockId = hot.Id,
                    SettlementDate = settlement,
                    CurrentShortPosition = 300_000,
                    DaysToCover = 8m,
                },
                new ShortInterest
                {
                    CommonStockId = cold.Id,
                    SettlementDate = settlement,
                    CurrentShortPosition = 50_000,
                    DaysToCover = 1m,
                }
            );
        await DbContext.SaveChangesAsync();

        var result = await Sut().GetShortSqueezeScores();

        result.Should().Contain("settlement 2026-04-15");
        result
            .IndexOf("| 1 | HOT |")
            .Should()
            .BeGreaterThan(0, "the harder-shorted stock ranks first");
        result.IndexOf("| 2 | COLD |").Should().BeGreaterThan(result.IndexOf("| 1 | HOT |"));
        result.Should().Contain("30.0");
    }

    [Fact]
    public async Task GetShortSqueezeScores_NoData_SaysSo()
    {
        var result = await Sut().GetShortSqueezeScores();

        result.Should().Contain("No short-squeeze scores available");
    }
}

using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.CorporateActions.Repositories;
using Equibles.Finra.BusinessLogic;
using Equibles.Finra.Data.Models;
using Equibles.Finra.Mcp.Tools;
using Equibles.Finra.Repositories;
using Equibles.IntegrationTests.Helpers;
using Equibles.Sec.Repositories;
using Equibles.Yahoo.Repositories;
using Xunit;

namespace Equibles.IntegrationTests.Mcp;

/// <summary>
/// Adversarial cover for the short-data tools' date arguments. The old
/// <c>McpToolExecutor.ParseDateOr</c> path silently substituted the default window for any
/// unparseable date and rendered a factual-sounding "no data" for an inverted range — both
/// make the tool answer a question the caller never asked. The strict contract is: a supplied
/// date must be ISO yyyy-MM-dd or the tool returns a one-line correction, and start &gt; end
/// is an explicit error, never an empty-range claim.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class ShortDataToolsStrictDateArgumentsTests : ParadeDbMcpTestBase
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
                new StockSplitRepository(DbContext),
                new FailToDeliverRepository(DbContext),
                new DailyStockPriceRepository(DbContext),
                []
            ),
            new StockSplitRepository(DbContext),
            ErrorManager,
            NullLogger<ShortDataTools>()
        );

    public ShortDataToolsStrictDateArgumentsTests(ParadeDbFixture fixture)
        : base(fixture) { }

    private async Task<CommonStock> SeedGmeWithVolume()
    {
        var stock = new CommonStock
        {
            Ticker = "GME",
            Name = "GameStop Corp",
            Cik = "0001326380",
        };
        DbContext.Set<CommonStock>().Add(stock);
        DbContext
            .Set<DailyShortVolume>()
            .Add(
                new DailyShortVolume
                {
                    CommonStock = stock,
                    CommonStockId = stock.Id,
                    Date = new DateOnly(2026, 4, 1),
                    ShortVolume = 1_000_000,
                    ShortExemptVolume = 0,
                    TotalVolume = 2_000_000,
                    Market = "ALL",
                }
            );
        await DbContext.SaveChangesAsync();
        return stock;
    }

    [Fact]
    public async Task GetShortVolume_UnparseableStartDate_ReturnsError()
    {
        await SeedGmeWithVolume();

        var result = await Sut().GetShortVolume("GME", startDate: "last month");

        result.Should().Be("Unknown startDate 'last month'. Accepted: yyyy-MM-dd.");
    }

    [Fact]
    public async Task GetShortVolume_UnparseableEndDate_ReturnsError()
    {
        await SeedGmeWithVolume();

        var result = await Sut().GetShortVolume("GME", endDate: "2026-13-05");

        result.Should().Be("Unknown endDate '2026-13-05'. Accepted: yyyy-MM-dd.");
    }

    [Fact]
    public async Task GetShortVolume_InvertedRange_ReturnsExplicitError()
    {
        await SeedGmeWithVolume();

        var result = await Sut()
            .GetShortVolume("GME", startDate: "2026-04-30", endDate: "2026-04-01");

        result
            .Should()
            .Contain("startDate 2026-04-30 is after endDate 2026-04-01")
            .And.NotContain("No short volume data");
    }

    [Fact]
    public async Task GetShortInterest_UnparseableStartDate_ReturnsError()
    {
        DbContext
            .Set<CommonStock>()
            .Add(
                new CommonStock
                {
                    Ticker = "GME",
                    Name = "GameStop Corp",
                    Cik = "0001326380",
                }
            );
        await DbContext.SaveChangesAsync();

        var result = await Sut().GetShortInterest("GME", startDate: "garbage");

        result.Should().Be("Unknown startDate 'garbage'. Accepted: yyyy-MM-dd.");
    }

    [Fact]
    public async Task GetShortInterest_InvertedRange_ReturnsExplicitError()
    {
        DbContext
            .Set<CommonStock>()
            .Add(
                new CommonStock
                {
                    Ticker = "GME",
                    Name = "GameStop Corp",
                    Cik = "0001326380",
                }
            );
        await DbContext.SaveChangesAsync();

        var result = await Sut()
            .GetShortInterest("GME", startDate: "2026-06-01", endDate: "2026-01-01");

        result
            .Should()
            .Contain("startDate 2026-06-01 is after endDate 2026-01-01")
            .And.NotContain("No short interest data");
    }

    [Fact]
    public async Task GetLargestShortVolume_UnparseableDate_ReturnsErrorInsteadOfLatestDay()
    {
        // The old ParseDateOr fallback answered with the LATEST day's leaderboard for a
        // typo'd date — the caller would present another day's data as the requested one.
        await SeedGmeWithVolume();

        var result = await Sut().GetLargestShortVolume(date: "garbage-date");

        result.Should().Be("Unknown date 'garbage-date'. Accepted: yyyy-MM-dd.");
    }

    [Fact]
    public async Task GetLargestShortVolume_UnknownSortBy_ReturnsError()
    {
        await SeedGmeWithVolume();

        var result = await Sut().GetLargestShortVolume(sortBy: "volume");

        result.Should().Be("Unknown sortBy 'volume'. Accepted: shortVolume, shortPercent.");
    }

    [Fact]
    public async Task GetShortInterestSnapshot_UnknownSortBy_ReturnsError()
    {
        var stock = new CommonStock
        {
            Ticker = "GME",
            Name = "GameStop Corp",
            Cik = "0001326380",
        };
        DbContext.Set<CommonStock>().Add(stock);
        DbContext
            .Set<ShortInterest>()
            .Add(
                new ShortInterest
                {
                    CommonStock = stock,
                    CommonStockId = stock.Id,
                    SettlementDate = new DateOnly(2026, 3, 15),
                    CurrentShortPosition = 1_000,
                    ChangeInShortPosition = 0,
                    AverageDailyVolume = 100,
                    DaysToCover = 10m,
                }
            );
        await DbContext.SaveChangesAsync();

        var result = await Sut().GetShortInterestSnapshot(sortBy: "position");

        result
            .Should()
            .Be("Unknown sortBy 'position'. Accepted: daysToCover, shortPosition, change.");
    }
}

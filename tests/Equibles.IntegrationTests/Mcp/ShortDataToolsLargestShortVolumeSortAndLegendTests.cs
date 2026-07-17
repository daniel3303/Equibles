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
/// Cover for GetLargestShortVolume's additive levers and identification columns: the
/// shortPercent sort with the minTotalVolume liquidity floor, the Company column (obscure
/// leaders like RGBP gave the model nothing to identify the issuer with), and the
/// truncation signpost.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class ShortDataToolsLargestShortVolumeSortAndLegendTests : ParadeDbMcpTestBase
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

    public ShortDataToolsLargestShortVolumeSortAndLegendTests(ParadeDbFixture fixture)
        : base(fixture) { }

    private static readonly DateOnly Day = new(2026, 4, 2);

    private int _nextCik = 1;

    private CommonStock AddStock(string ticker, string name)
    {
        var stock = new CommonStock
        {
            Ticker = ticker,
            Name = name,
            Cik = (_nextCik++).ToString("D10"),
        };
        DbContext.Set<CommonStock>().Add(stock);
        return stock;
    }

    private void AddVolume(CommonStock stock, long shortVolume, long totalVolume) =>
        DbContext
            .Set<DailyShortVolume>()
            .Add(
                new DailyShortVolume
                {
                    CommonStock = stock,
                    CommonStockId = stock.Id,
                    Date = Day,
                    ShortVolume = shortVolume,
                    ShortExemptVolume = 0,
                    TotalVolume = totalVolume,
                    Market = "ALL",
                }
            );

    [Fact]
    public async Task LargestShortVolume_RowsCarryCompanyName()
    {
        AddVolume(AddStock("RGBP", "Regen BioPharma Inc"), 5_000_000, 8_000_000);
        await DbContext.SaveChangesAsync();

        var result = await Sut().GetLargestShortVolume();

        result.Should().Contain("| RGBP | Regen BioPharma Inc |");
    }

    [Fact]
    public async Task LargestShortVolume_SortByShortPercent_RanksByIntensity()
    {
        // GME: 90% of a small tape; AMC: 45% of a big one. Percent sort must lead with GME
        // even though AMC's absolute short volume is larger.
        AddVolume(AddStock("GME", "GameStop Corp"), 900_000, 1_000_000);
        AddVolume(AddStock("AMC", "AMC Entertainment"), 9_000_000, 20_000_000);
        await DbContext.SaveChangesAsync();

        var result = await Sut().GetLargestShortVolume(sortBy: "shortPercent");

        result.IndexOf("GME").Should().BeLessThan(result.IndexOf("AMC"));
    }

    [Fact]
    public async Task LargestShortVolume_MinTotalVolume_DropsIlliquidNames()
    {
        AddVolume(AddStock("GME", "GameStop Corp"), 900_000, 1_000_000);
        AddVolume(AddStock("TINY", "Tiny Illiquid Corp"), 98, 100);
        await DbContext.SaveChangesAsync();

        var result = await Sut()
            .GetLargestShortVolume(sortBy: "shortPercent", minTotalVolume: 500_000);

        result.Should().Contain("GME");
        result.Should().NotContain("TINY");
    }

    [Fact]
    public async Task LargestShortVolume_TruncatedResults_AppendTruncationNote()
    {
        AddVolume(AddStock("GME", "GameStop Corp"), 5_000_000, 8_000_000);
        AddVolume(AddStock("AMC", "AMC Entertainment"), 9_000_000, 20_000_000);
        await DbContext.SaveChangesAsync();

        var result = await Sut().GetLargestShortVolume(maxResults: 1);

        result.Should().Contain("Showing first 1 of 2 results");
    }
}

using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.CorporateActions.Data.Models;
using Equibles.IntegrationTests.Helpers;
using Equibles.Yahoo.Data.Models;
using Equibles.Yahoo.Mcp.Tools;
using Equibles.Yahoo.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Equibles.IntegrationTests.Mcp;

/// <summary>
/// One CommonStock row is one SEC filer, and the filer's other listed symbols ride along
/// in SecondaryTickers — sibling share classes, warrants, units, separate fund series.
/// The primary series remains attached to the filer while each authoritative secondary
/// symbol has its own keyed rows.
///
/// In production that made BRK-A report BRK-B's close (the two are fixed at 1500:1 by
/// charter). These pin independent resolution end-to-end through the real repository.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class StockPriceToolsSecondaryTickerTests : ParadeDbMcpTestBase
{
    public StockPriceToolsSecondaryTickerTests(ParadeDbFixture fixture)
        : base(fixture) { }

    private StockPriceTools Sut() =>
        new(
            new DailyStockPriceRepository(DbContext),
            new CommonStockRepository(DbContext),
            new Equibles.CorporateActions.Repositories.StockSplitRepository(DbContext),
            ErrorManager,
            NullLogger<StockPriceTools>()
        );

    private async Task<CommonStock> SeedBerkshire()
    {
        var stock = new CommonStock
        {
            Ticker = "BRK-B",
            Name = "Berkshire Hathaway Inc",
            Cik = "0001067983",
            SecondaryTickers = ["BRK-A"],
        };
        DbContext.Set<CommonStock>().Add(stock);
        await DbContext.SaveChangesAsync();

        DbContext
            .Set<DailyStockPrice>()
            .AddRange(
                new DailyStockPrice
                {
                    CommonStockId = stock.Id,
                    ListedTicker = "BRK-B",
                    Date = new DateOnly(2026, 7, 31),
                    Open = 510m,
                    High = 512m,
                    Low = 509m,
                    Close = 511.54m,
                    AdjustedClose = 511.54m,
                    Volume = 3_934_400,
                },
                new DailyStockPrice
                {
                    CommonStockId = stock.Id,
                    ListedTicker = "BRK-A",
                    Date = new DateOnly(2026, 7, 31),
                    Open = 748_500m,
                    High = 750_000m,
                    Low = 747_000m,
                    Close = 749_200m,
                    AdjustedClose = 749_200m,
                    Volume = 1_230,
                }
            );
        await DbContext.SaveChangesAsync();
        return stock;
    }

    [Fact]
    public async Task GetLatestPrices_SecondaryAndPrimarySymbolsReportTheirOwnPrices()
    {
        await SeedBerkshire();

        var result = await Sut().GetLatestPrices("BRK-A,BRK-B");

        result.Should().Contain("| BRK-B | 2026-07-31 | 511.54 |", "the primary still answers");
        result.Should().Contain("| BRK-A | 2026-07-31 | 749200.00 |", "Class A has its own series");
        result.Should().NotContain("No series");
    }

    [Fact]
    public async Task GetLatestPrices_LegacyPrimarySplit_DoesNotClipSecondaryRange()
    {
        var stock = await SeedBerkshire();
        DbContext
            .Set<DailyStockPrice>()
            .Add(
                new DailyStockPrice
                {
                    CommonStockId = stock.Id,
                    ListedTicker = "BRK-A",
                    Date = new DateOnly(2025, 8, 1),
                    Open = 900_000m,
                    High = 900_000m,
                    Low = 900_000m,
                    Close = 900_000m,
                    AdjustedClose = 900_000m,
                    Volume = 100,
                }
            );
        DbContext
            .Set<StockSplit>()
            .Add(
                new StockSplit
                {
                    CommonStockId = stock.Id,
                    PriceSeriesTicker = null,
                    EffectiveDate = new DateOnly(2026, 1, 2),
                    Numerator = 2m,
                    Denominator = 1m,
                    Source = StockSplitSource.Yahoo,
                }
            );
        await DbContext.SaveChangesAsync();

        var result = await Sut().GetLatestPrices("BRK-A");

        result.Should().Contain("| 900000.00 | 749200.00 |");
        result.Should().NotContain("latest recorded split");
    }

    [Fact]
    public async Task GetLatestPrices_SecondarySplit_ClipsOnlyThatSecondaryRange()
    {
        var stock = await SeedBerkshire();
        DbContext
            .Set<DailyStockPrice>()
            .Add(
                new DailyStockPrice
                {
                    CommonStockId = stock.Id,
                    ListedTicker = "BRK-A",
                    Date = new DateOnly(2025, 8, 1),
                    Open = 900_000m,
                    High = 900_000m,
                    Low = 900_000m,
                    Close = 900_000m,
                    AdjustedClose = 900_000m,
                    Volume = 100,
                }
            );
        DbContext
            .Set<StockSplit>()
            .Add(
                new StockSplit
                {
                    CommonStockId = stock.Id,
                    PriceSeriesTicker = "BRK-A",
                    EffectiveDate = new DateOnly(2026, 1, 2),
                    Numerator = 2m,
                    Denominator = 1m,
                    Source = StockSplitSource.Yahoo,
                }
            );
        await DbContext.SaveChangesAsync();

        var result = await Sut().GetLatestPrices("BRK-A");

        result.Should().Contain("| 749200.00\\* | 749200.00\\* |");
        result.Should().NotContain("900000.00");
        result.Should().Contain("latest recorded split");
    }

    [Fact]
    public async Task GetStockPrices_ASecondarySymbol_ServesItsOwnSeries()
    {
        await SeedBerkshire();

        var result = await Sut().GetStockPrices("BRK-A");

        result.Should().Contain("Daily prices for BRK-A");
        result.Should().Contain("749200.00");
        result.Should().NotContain("511.54");
    }

    [Fact]
    public async Task LegacyTableRow_CanCoexistButIsNeverPublished()
    {
        var stock = await SeedBerkshire();
        var legacyId = Guid.NewGuid();
        var date = new DateOnly(2026, 7, 31);
        var createdAt = DateTime.UtcNow;
        await DbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO "DailyStockPrice"
                ("Id", "CommonStockId", "Date", "Open", "High", "Low",
                 "Close", "AdjustedClose", "Volume", "CreationTime")
            VALUES
                ({legacyId}, {stock.Id}, {date}, {999m}, {999m}, {999m},
                 {999m}, {999m}, {1L}, {createdAt})
            """
        );

        var result = await Sut().GetStockPrices("BRK-B");

        result.Should().Contain("511.54");
        result.Should().NotContain("999.00");
    }

    [Fact]
    public async Task GetStockPrices_ThePrimaryInDotNotation_StillResolves()
    {
        await SeedBerkshire();

        // The dot form is a spelling of the SAME symbol, so the refusal must not catch it.
        var result = await Sut().GetStockPrices("BRK.B");

        result.Should().Contain("511.54");
        result.Should().NotContain("749200.00");
    }

    [Fact]
    public async Task GetStockPrices_ASecondarySymbolInDotNotation_UsesTheSecondarySeries()
    {
        await SeedBerkshire();

        var result = await Sut().GetStockPrices("BRK.A");

        result.Should().Contain("Daily prices for BRK-A");
        result.Should().Contain("749200.00");
        result.Should().NotContain("511.54");
    }

    [Fact]
    public async Task GetBollingerBands_ASecondarySymbol_UsesTheSecondarySeries()
    {
        await SeedBerkshire();

        // Every indicator shares one resolution path, so none can drift back to primary bars.
        var result = await Sut().GetBollingerBands("BRK-A");

        result.Should().Contain("for BRK-A");
        result.Should().Contain("749200.00");
        result.Should().NotContain("511.54");
    }
}

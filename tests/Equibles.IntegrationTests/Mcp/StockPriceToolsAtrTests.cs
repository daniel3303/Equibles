using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.IntegrationTests.Helpers;
using Equibles.Yahoo.Data.Models;
using Equibles.Yahoo.Mcp.Tools;
using Equibles.Yahoo.Repositories;
using Xunit;

namespace Equibles.IntegrationTests.Mcp;

/// <summary>
/// Pins the GetAverageTrueRange MCP tool end-to-end against ParadeDB so repository +
/// projection + calculator + Markdown formatting all run through the real pipeline.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class StockPriceToolsAtrTests : ParadeDbMcpTestBase
{
    public StockPriceToolsAtrTests(ParadeDbFixture fixture)
        : base(fixture) { }

    private StockPriceTools Sut() =>
        new(
            new DailyStockPriceRepository(DbContext),
            new CommonStockRepository(DbContext),
            ErrorManager,
            NullLogger<StockPriceTools>()
        );

    [Fact]
    public async Task GetAverageTrueRange_UnknownTicker_ReturnsNotFoundMessage()
    {
        var result = await Sut().GetAverageTrueRange("ZZZZ");

        result.Should().Be("Stock 'ZZZZ' not found.");
    }

    [Fact]
    public async Task GetAverageTrueRange_NoPrices_ReturnsEmptyRangeMessage()
    {
        DbContext.Set<CommonStock>().Add(MakeStock());
        await DbContext.SaveChangesAsync();

        var result = await Sut()
            .GetAverageTrueRange("AAPL", startDate: "2026-04-01", endDate: "2026-04-30");

        result.Should().Contain("No price data found for AAPL");
    }

    [Fact]
    public async Task GetAverageTrueRange_InvalidPeriod_ReturnsValidationMessage()
    {
        var result = await Sut().GetAverageTrueRange("AAPL", period: 1);

        result.Should().Contain("period must be at least 2");
    }

    [Fact]
    public async Task GetAverageTrueRange_FlatOhlc_ReturnsZeroAtrAfterWarmUp()
    {
        var stock = MakeStock();
        DbContext.Set<CommonStock>().Add(stock);
        await DbContext.SaveChangesAsync();

        // 20 identical bars → TR is 0 throughout → ATR is 0 after the seed lands.
        var start = new DateOnly(2025, 1, 6);
        for (var i = 0; i < 20; i++)
        {
            DbContext
                .Set<DailyStockPrice>()
                .Add(
                    new DailyStockPrice
                    {
                        CommonStockId = stock.Id,
                        Date = start.AddDays(i),
                        Open = 100m,
                        High = 100m,
                        Low = 100m,
                        Close = 100m,
                        AdjustedClose = 100m,
                        Volume = 1,
                    }
                );
        }
        await DbContext.SaveChangesAsync();

        var result = await Sut()
            .GetAverageTrueRange(
                "AAPL",
                startDate: start.ToString("yyyy-MM-dd"),
                endDate: start.AddDays(20).ToString("yyyy-MM-dd")
            );

        result.Should().Contain("Average True Range");
        result.Should().Contain("| Date | Close | ATR |");
        // The latest bar is index 19 (well past the period=14 seed at index 13) → ATR = 0.
        result.Should().Contain("0.0000");
    }

    [Fact]
    public async Task GetAverageTrueRange_MaxResults_LimitsRowCount()
    {
        var stock = MakeStock();
        DbContext.Set<CommonStock>().Add(stock);
        await DbContext.SaveChangesAsync();
        var start = new DateOnly(2025, 1, 6);
        for (var i = 0; i < 30; i++)
        {
            DbContext
                .Set<DailyStockPrice>()
                .Add(
                    new DailyStockPrice
                    {
                        CommonStockId = stock.Id,
                        Date = start.AddDays(i),
                        Open = 100m,
                        High = 101m,
                        Low = 99m,
                        Close = 100m,
                        AdjustedClose = 100m,
                        Volume = 1,
                    }
                );
        }
        await DbContext.SaveChangesAsync();

        var result = await Sut()
            .GetAverageTrueRange(
                "AAPL",
                startDate: start.ToString("yyyy-MM-dd"),
                endDate: start.AddDays(30).ToString("yyyy-MM-dd"),
                maxResults: 5
            );

        var dataLines = result.Split('\n').Count(line => line.StartsWith("| 2025-"));
        dataLines.Should().Be(5);
    }

    private static CommonStock MakeStock() =>
        new()
        {
            Ticker = "AAPL",
            Name = "Apple Inc",
            Cik = "0000320193",
        };
}

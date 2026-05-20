using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.IntegrationTests.Helpers;
using Equibles.Yahoo.Data.Models;
using Equibles.Yahoo.Mcp.Tools;
using Equibles.Yahoo.Repositories;
using Xunit;

namespace Equibles.IntegrationTests.Mcp;

/// <summary>
/// Pins the GetOnBalanceVolume MCP tool end-to-end against ParadeDB so repository +
/// projection + calculator + Markdown formatting all run through the real pipeline.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class StockPriceToolsObvTests : ParadeDbMcpTestBase
{
    public StockPriceToolsObvTests(ParadeDbFixture fixture)
        : base(fixture) { }

    private StockPriceTools Sut() =>
        new(
            new DailyStockPriceRepository(DbContext),
            new CommonStockRepository(DbContext),
            ErrorManager,
            NullLogger<StockPriceTools>()
        );

    [Fact]
    public async Task GetOnBalanceVolume_UnknownTicker_ReturnsNotFoundMessage()
    {
        var result = await Sut().GetOnBalanceVolume("ZZZZ");

        result.Should().Be("Stock 'ZZZZ' not found.");
    }

    [Fact]
    public async Task GetOnBalanceVolume_NoPrices_ReturnsEmptyRangeMessage()
    {
        DbContext.Set<CommonStock>().Add(MakeStock());
        await DbContext.SaveChangesAsync();

        var result = await Sut()
            .GetOnBalanceVolume("AAPL", startDate: "2026-04-01", endDate: "2026-04-30");

        result.Should().Contain("No price data found for AAPL");
    }

    [Fact]
    public async Task GetOnBalanceVolume_StrictlyRising_AccumulatesAllVolumes()
    {
        var stock = MakeStock();
        DbContext.Set<CommonStock>().Add(stock);
        await DbContext.SaveChangesAsync();

        // Bars 0..4 with closes 10,11,12,13,14 and volumes 100,200,300,400,500.
        // OBV at bar 4 = 0 + 200 + 300 + 400 + 500 = 1,400.
        var start = new DateOnly(2025, 1, 6);
        var closes = new[] { 10m, 11m, 12m, 13m, 14m };
        var volumes = new long[] { 100, 200, 300, 400, 500 };
        for (var i = 0; i < closes.Length; i++)
        {
            DbContext
                .Set<DailyStockPrice>()
                .Add(
                    new DailyStockPrice
                    {
                        CommonStockId = stock.Id,
                        Date = start.AddDays(i),
                        Open = closes[i],
                        High = closes[i],
                        Low = closes[i],
                        Close = closes[i],
                        AdjustedClose = closes[i],
                        Volume = volumes[i],
                    }
                );
        }
        await DbContext.SaveChangesAsync();

        var result = await Sut()
            .GetOnBalanceVolume(
                "AAPL",
                startDate: start.ToString("yyyy-MM-dd"),
                endDate: start.AddDays(5).ToString("yyyy-MM-dd")
            );

        result.Should().Contain("On-Balance Volume");
        result.Should().Contain("| Date | Close | Volume | OBV |");
        // Culture-tolerant: thousand separator may be `,` (US/UK), `.` (DE),
        // ` ` (FR), or NBSP. Anchor on the digits.
        var digitsOnly = new string(result.Where(char.IsDigit).ToArray());
        digitsOnly.Should().Contain("1400");
    }

    [Fact]
    public async Task GetOnBalanceVolume_MaxResults_LimitsRowCount()
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
                        Close = 100m + i,
                        AdjustedClose = 100m,
                        Volume = 1_000,
                    }
                );
        }
        await DbContext.SaveChangesAsync();

        var result = await Sut()
            .GetOnBalanceVolume(
                "AAPL",
                startDate: start.ToString("yyyy-MM-dd"),
                endDate: start.AddDays(30).ToString("yyyy-MM-dd"),
                maxResults: 5
            );

        var dataLines = result.Split('\n').Count(line => line.StartsWith("| 2025-"));
        dataLines.Should().Be(5);
    }

    [Fact]
    public async Task GetOnBalanceVolume_EmitsRowsNewestFirst()
    {
        // Tool description promises "(default: 60, newest first)". None of the other
        // tests pin the row order — a regression that flipped the loop direction would
        // still pass them. Five increasing dates; first data row in the table must be
        // the latest one.
        var stock = MakeStock();
        DbContext.Set<CommonStock>().Add(stock);
        await DbContext.SaveChangesAsync();
        var start = new DateOnly(2025, 1, 6);
        for (var i = 0; i < 5; i++)
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
                        Close = 100m + i,
                        AdjustedClose = 100m,
                        Volume = 1_000,
                    }
                );
        }
        await DbContext.SaveChangesAsync();

        var result = await Sut()
            .GetOnBalanceVolume(
                "AAPL",
                startDate: start.ToString("yyyy-MM-dd"),
                endDate: start.AddDays(5).ToString("yyyy-MM-dd")
            );

        var firstDataRow = result.Split('\n').First(line => line.StartsWith("| 2025-"));
        firstDataRow.Should().StartWith($"| {start.AddDays(4):yyyy-MM-dd} |");
    }

    private static CommonStock MakeStock() =>
        new()
        {
            Ticker = "AAPL",
            Name = "Apple Inc",
            Cik = "0000320193",
        };
}

using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.IntegrationTests.Helpers;
using Equibles.Yahoo.Data.Models;
using Equibles.Yahoo.Mcp.Tools;
using Equibles.Yahoo.Repositories;
using Xunit;

namespace Equibles.IntegrationTests.Mcp;

/// <summary>
/// Pins the GetStochasticOscillator MCP tool end-to-end against ParadeDB so
/// repository + projection + calculator + Markdown formatting all run through the
/// real pipeline.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class StockPriceToolsStochasticTests : ParadeDbMcpTestBase
{
    public StockPriceToolsStochasticTests(ParadeDbFixture fixture)
        : base(fixture) { }

    private StockPriceTools Sut() =>
        new(
            new DailyStockPriceRepository(DbContext),
            new CommonStockRepository(DbContext),
            ErrorManager,
            NullLogger<StockPriceTools>()
        );

    [Fact]
    public async Task GetStochasticOscillator_UnknownTicker_ReturnsNotFoundMessage()
    {
        var result = await Sut().GetStochasticOscillator("ZZZZ");

        result.Should().Be("Stock 'ZZZZ' not found.");
    }

    [Fact]
    public async Task GetStochasticOscillator_NoPrices_ReturnsEmptyRangeMessage()
    {
        DbContext.Set<CommonStock>().Add(MakeStock());
        await DbContext.SaveChangesAsync();

        var result = await Sut()
            .GetStochasticOscillator("AAPL", startDate: "2026-04-01", endDate: "2026-04-30");

        result.Should().Contain("No price data found for AAPL");
    }

    [Fact]
    public async Task GetStochasticOscillator_InvalidPeriods_ReturnsValidationMessage()
    {
        var result = await Sut().GetStochasticOscillator("AAPL", kPeriod: 1);

        result.Should().Contain("kPeriod must be at least 2");
    }

    [Fact]
    public async Task GetStochasticOscillator_RisingPrices_ReturnsTableWithKAndDValues()
    {
        var stock = MakeStock();
        DbContext.Set<CommonStock>().Add(stock);
        await DbContext.SaveChangesAsync();

        // 20 strictly-increasing daily bars; the 14/3 default lookback fills around
        // index 15 and %K trends toward 100 as the close stays at the lookback high.
        var start = new DateOnly(2025, 1, 6); // a Monday so the date range covers ~4 weeks
        for (var i = 0; i < 20; i++)
        {
            DbContext
                .Set<DailyStockPrice>()
                .Add(
                    new DailyStockPrice
                    {
                        CommonStockId = stock.Id,
                        Date = start.AddDays(i),
                        Open = 100m + i,
                        High = 100.5m + i,
                        Low = 99.5m + i,
                        Close = 100m + i,
                        AdjustedClose = 100m + i,
                        Volume = 1_000_000,
                    }
                );
        }
        await DbContext.SaveChangesAsync();

        var result = await Sut()
            .GetStochasticOscillator(
                "AAPL",
                startDate: start.ToString("yyyy-MM-dd"),
                endDate: start.AddDays(20).ToString("yyyy-MM-dd")
            );

        result.Should().Contain("Stochastic Oscillator");
        result.Should().Contain("| Date | Close | %K | %D |");
        // Latest bar's %K should be at or near 100 since price is the lookback high.
        // Allow culture-invariant formatting (always F2 → "100.00").
        result.Should().Contain("100.00");
    }

    [Fact]
    public async Task GetStochasticOscillator_MaxResults_LimitsRowCount()
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
            .GetStochasticOscillator(
                "AAPL",
                startDate: start.ToString("yyyy-MM-dd"),
                endDate: start.AddDays(30).ToString("yyyy-MM-dd"),
                maxResults: 5
            );

        // Header row + separator + 5 data rows = at most 5 lines starting with "| 2025-"
        var dataLines = result.Split('\n').Count(line => line.StartsWith("| 2025-"));
        dataLines.Should().Be(5);
    }

    [Fact]
    public async Task GetStochasticOscillator_DPeriodBelowOne_ReturnsValidationMessage()
    {
        // Validation guards both periods: kPeriod < 2 || dPeriod < 1. The existing
        // InvalidPeriods test only exercises the kPeriod side — a regression that dropped
        // the dPeriod check (e.g. trimmed the `||` clause) would still pass it.
        var result = await Sut().GetStochasticOscillator("AAPL", kPeriod: 14, dPeriod: 0);

        result.Should().Contain("dPeriod at least 1");
    }

    [Fact]
    public async Task GetStochasticOscillator_EmitsRowsNewestFirst()
    {
        // Tool description promises "(default: 60, newest first)". None of the other
        // Stochastic tests pin the row order — a regression that flipped the loop
        // direction (i = 0 → i < records.Count) would still pass them. Five
        // increasing dates; first data row in the table must be the latest one.
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
                        Close = 100m,
                        AdjustedClose = 100m,
                        Volume = 1,
                    }
                );
        }
        await DbContext.SaveChangesAsync();

        var result = await Sut()
            .GetStochasticOscillator(
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

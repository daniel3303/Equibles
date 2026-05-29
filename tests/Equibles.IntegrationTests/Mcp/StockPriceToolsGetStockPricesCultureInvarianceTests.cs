using System.Globalization;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.IntegrationTests.Helpers;
using Equibles.Yahoo.Data.Models;
using Equibles.Yahoo.Mcp.Tools;
using Equibles.Yahoo.Repositories;
using Xunit;

namespace Equibles.IntegrationTests.Mcp;

[Collection(ParadeDbCollection.Name)]
public class StockPriceToolsGetStockPricesCultureInvarianceTests : ParadeDbMcpTestBase
{
    private StockPriceTools Sut() =>
        new(
            new DailyStockPriceRepository(DbContext),
            new CommonStockRepository(DbContext),
            ErrorManager,
            NullLogger<StockPriceTools>()
        );

    public StockPriceToolsGetStockPricesCultureInvarianceTests(ParadeDbFixture fixture)
        : base(fixture) { }

    // GetStockPrices renders the Volume cell with the culture-implicit :N0 specifier
    // (and the OHLC cells with :F2), which honour the thread CurrentCulture. The
    // established repo contract (the dozens of InvariantCulture call sites across the
    // MCP tools commenting "MCP markdown must not fork the separators by host locale")
    // is that the LLM-facing markdown renders the same on every host. de-DE swaps the
    // thousand separator (1,234,567 → 1.234.567), forking the response — same bug
    // class as the fixed Holdings render methods (#2628).
    [Fact(Skip = "GH-2783 — GetStockPrices :F2/:N0 cells follow host CurrentCulture")]
    public async Task GetStockPrices_UnderNonInvariantCulture_RendersVolumeCultureInvariantly()
    {
        var stock = new CommonStock
        {
            Ticker = "AAPL",
            Name = "Apple Inc",
            Cik = "0000320193",
        };
        var price = new DailyStockPrice
        {
            CommonStock = stock,
            CommonStockId = stock.Id,
            Date = new DateOnly(2026, 3, 15),
            Open = 149m,
            High = 151m,
            Low = 148m,
            Close = 150m,
            AdjustedClose = 150m,
            Volume = 1_234_567,
        };
        DbContext.Set<CommonStock>().Add(stock);
        DbContext.Set<DailyStockPrice>().Add(price);
        await DbContext.SaveChangesAsync();

        // Pin de-DE only for the rendering call; CurrentCulture flows through the
        // tool's await chain via ExecutionContext. Base class restores invariant.
        var previous = CultureInfo.CurrentCulture;
        string result;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            result = await Sut().GetStockPrices("AAPL");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }

        // 1,234,567 shares of volume must render with en-US grouping on every host locale.
        result.Should().Contain("| 1,234,567 |");
    }
}

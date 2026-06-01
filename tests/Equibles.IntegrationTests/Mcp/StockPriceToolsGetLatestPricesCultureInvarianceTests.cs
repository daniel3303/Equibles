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
public class StockPriceToolsGetLatestPricesCultureInvarianceTests : ParadeDbMcpTestBase
{
    private StockPriceTools Sut() =>
        new(
            new DailyStockPriceRepository(DbContext),
            new CommonStockRepository(DbContext),
            ErrorManager,
            NullLogger<StockPriceTools>()
        );

    public StockPriceToolsGetLatestPricesCultureInvarianceTests(ParadeDbFixture fixture)
        : base(fixture) { }

    // Contract (the repo-wide MCP rule, asserted by McpFormat and the dozens of
    // InvariantCulture call sites): LLM-facing markdown must render numbers the same
    // on every host locale. GetLatestPrices renders its Close (:F2) and Volume (:N0)
    // cells with the culture-implicit specifiers, which honour the thread
    // CurrentCulture — so de-DE swaps the decimal point and thousand separator,
    // forking the response. Same bug class as the already-fixed GetStockPrices (#2628).
    [Fact]
    public async Task GetLatestPrices_UnderNonInvariantCulture_RendersCloseAndVolumeCultureInvariantly()
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
            result = await Sut().GetLatestPrices("AAPL");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }

        // Close (:F2, decimal point) and Volume (:N0, thousand comma) must render with
        // en-US separators on any host locale. de-DE would produce 150,00 and 1.234.567.
        result.Should().Contain("| 150.00 |");
        result.Should().Contain("| 1,234,567 |");
    }
}

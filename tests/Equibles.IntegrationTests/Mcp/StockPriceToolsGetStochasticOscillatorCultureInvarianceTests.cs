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
public class StockPriceToolsGetStochasticOscillatorCultureInvarianceTests : ParadeDbMcpTestBase
{
    private StockPriceTools Sut() =>
        new(
            new DailyStockPriceRepository(DbContext),
            new CommonStockRepository(DbContext),
            ErrorManager,
            NullLogger<StockPriceTools>()
        );

    public StockPriceToolsGetStochasticOscillatorCultureInvarianceTests(ParadeDbFixture fixture)
        : base(fixture) { }

    // Contract (the repo-wide MCP rule, asserted by McpFormat and the InvariantCulture call
    // sites): LLM-facing markdown must render numbers identically on every host locale. The
    // %K/%D cells already route through McpFormat.OrDash (invariant), but the Close cell uses
    // the culture-implicit :F2 specifier, so de-DE renders the decimal comma — forking the
    // response and making one column disagree with the next. Same bug class as GetLatestPrices
    // (#3100) and the already-fixed GetStockPrices (#2628).
    [Fact]
    public async Task GetStochasticOscillator_UnderNonInvariantCulture_RendersCloseCultureInvariantly()
    {
        var stock = new CommonStock
        {
            Ticker = "AAPL",
            Name = "Apple Inc",
            Cik = "0000320193",
        };
        DbContext.Set<CommonStock>().Add(stock);
        await DbContext.SaveChangesAsync();

        // 20 bars with a constant Close of 123.45 (above the 0-100 %K/%D range, so the
        // invariant "123.45" cannot coincide with an oscillator cell). High/Low straddle it
        // so the %K range is non-zero.
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
                        Open = 123.45m,
                        High = 124m,
                        Low = 123m,
                        Close = 123.45m,
                        AdjustedClose = 123.45m,
                        Volume = 1_000_000,
                    }
                );
        }
        await DbContext.SaveChangesAsync();

        // Pin de-DE only for the rendering call; CurrentCulture flows through the tool's
        // await chain via ExecutionContext. Base class restores invariant.
        var previous = CultureInfo.CurrentCulture;
        string result;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            result = await Sut()
                .GetStochasticOscillator(
                    "AAPL",
                    startDate: start.ToString("yyyy-MM-dd"),
                    endDate: start.AddDays(20).ToString("yyyy-MM-dd")
                );
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }

        // The Close cell must render with an en-US decimal point on any host locale.
        // de-DE would produce "123,45".
        result.Should().Contain("| 123.45 |");
    }
}

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
public class StockPriceToolsGetBollingerBandsCultureInvarianceTests : ParadeDbMcpTestBase
{
    private StockPriceTools Sut() =>
        new(
            new DailyStockPriceRepository(DbContext),
            new CommonStockRepository(DbContext),
            ErrorManager,
            NullLogger<StockPriceTools>()
        );

    public StockPriceToolsGetBollingerBandsCultureInvarianceTests(ParadeDbFixture fixture)
        : base(fixture) { }

    // Contract (the repo-wide MCP rule, asserted by McpFormat and the InvariantCulture call
    // sites): LLM-facing markdown must render numbers identically on every host locale. The
    // Lower/Middle/Upper band cells already route through McpFormat.OrDash (invariant), but the
    // Close cell uses the culture-implicit :F2 specifier, so de-DE renders the decimal comma —
    // forking the response and making the Close column disagree with the band columns. Same bug
    // class as GetStochasticOscillator / GetAverageTrueRange / GetOnBalanceVolume (#3103, the
    // "sibling indicator tools") and GetLatestPrices (#3100).
    [Fact(
        Skip = "GH-3103 — GetBollingerBands renders the Close column with a culture-implicit specifier"
    )]
    public async Task GetBollingerBands_UnderNonInvariantCulture_RendersCloseCultureInvariantly()
    {
        var stock = new CommonStock
        {
            Ticker = "AAPL",
            Name = "Apple Inc",
            Cik = "0000320193",
        };
        DbContext.Set<CommonStock>().Add(stock);
        await DbContext.SaveChangesAsync();

        // 20 bars for the default period=20 window. The oldest bar's Close is 123.45 — it lands
        // in a row whose bands are still warming up (dashes), so no band cell in that row can
        // coincidentally render the invariant "123.45". Every other Close is 100.00, and the one
        // computed band row (the newest bar) averages ~101 — neither can produce "| 123.45 |".
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
                        Close = i == 0 ? 123.45m : 100m,
                        AdjustedClose = i == 0 ? 123.45m : 100m,
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
                .GetBollingerBands(
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

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
public class StockPriceToolsGetOnBalanceVolumeCultureInvarianceTests : ParadeDbMcpTestBase
{
    private StockPriceTools Sut() =>
        new(
            new DailyStockPriceRepository(DbContext),
            new CommonStockRepository(DbContext),
            ErrorManager,
            NullLogger<StockPriceTools>()
        );

    public StockPriceToolsGetOnBalanceVolumeCultureInvarianceTests(ParadeDbFixture fixture)
        : base(fixture) { }

    // Contract (the repo-wide MCP rule, asserted by McpFormat and the InvariantCulture call
    // sites): LLM-facing markdown must render numbers identically on every host locale. The
    // OBV row builds the Close cell with the culture-implicit :F2 specifier
    // (StockPriceTools.cs:337), so de-DE renders the decimal comma — forking the response by
    // host locale. Same bug class and sibling tool enumerated in GH-3103 (alongside the
    // already-pinned GetStochasticOscillator (#3104) and GetAverageTrueRange (#3108)).
    [Fact(
        Skip = "GH-3103 — GetOnBalanceVolume renders the Close column with a culture-implicit specifier"
    )]
    public async Task GetOnBalanceVolume_UnderNonInvariantCulture_RendersCloseCultureInvariantly()
    {
        var stock = new CommonStock
        {
            Ticker = "AAPL",
            Name = "Apple Inc",
            Cik = "0000320193",
        };
        DbContext.Set<CommonStock>().Add(stock);
        await DbContext.SaveChangesAsync();

        // 20 bars with a constant Close of 123.45 so the Close cell renders a fixed value that
        // can never coincide with the Volume/OBV columns.
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
                .GetOnBalanceVolume(
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

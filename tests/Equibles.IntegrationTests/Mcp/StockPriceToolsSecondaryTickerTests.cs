using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.IntegrationTests.Helpers;
using Equibles.Yahoo.Data.Models;
using Equibles.Yahoo.Mcp.Tools;
using Equibles.Yahoo.Repositories;
using Xunit;

namespace Equibles.IntegrationTests.Mcp;

/// <summary>
/// One CommonStock row is one SEC filer, and the filer's other listed symbols ride along
/// in SecondaryTickers — sibling share classes, warrants, units, separate fund series.
/// The row holds ONE price series, fetched under the primary symbol, so a lookup that
/// accepts either spelling answered a secondary symbol with the primary's bars.
///
/// In production that made BRK-A report BRK-B's close (the two are fixed at 1500:1 by
/// charter) and BWET, the Breakwave Tanker fund, report BDRY's — identical close, change
/// and volume. These pin the refusal end-to-end through the real repository.
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
            .Add(
                new DailyStockPrice
                {
                    CommonStockId = stock.Id,
                    Date = new DateOnly(2026, 7, 31),
                    Open = 510m,
                    High = 512m,
                    Low = 509m,
                    Close = 511.54m,
                    AdjustedClose = 511.54m,
                    Volume = 3_934_400,
                }
            );
        await DbContext.SaveChangesAsync();
        return stock;
    }

    [Fact]
    public async Task GetLatestPrices_ASecondarySymbol_DoesNotReportThePrimarysPrice()
    {
        await SeedBerkshire();

        var result = await Sut().GetLatestPrices("BRK-A,BRK-B");

        result.Should().Contain("| BRK-B | 2026-07-31 | 511.54 |", "the primary still answers");
        result
            .Should()
            .Contain(
                "No series — secondary symbol on BRK-B",
                "the row must say which symbol owns the series, not carry a price"
            );
        result
            .Should()
            .NotContain(
                "| BRK-A | 2026-07-31 | 511.54 |",
                "Class A must never be served Class B's close"
            );
    }

    [Fact]
    public async Task GetStockPrices_ASecondarySymbol_ExplainsInsteadOfServingTheSeries()
    {
        await SeedBerkshire();

        var result = await Sut().GetStockPrices("BRK-A");

        result.Should().Contain("No price series for 'BRK-A'");
        result.Should().Contain("BRK-B", "the caller needs the symbol that does carry it");
        result.Should().NotContain("511.54");
    }

    [Fact]
    public async Task GetStockPrices_ThePrimaryInDotNotation_StillResolves()
    {
        await SeedBerkshire();

        // The dot form is a spelling of the SAME symbol, so the refusal must not catch it.
        var result = await Sut().GetStockPrices("BRK.B");

        result.Should().Contain("511.54");
        result.Should().NotContain("No price series");
    }

    [Fact]
    public async Task GetStockPrices_ASecondarySymbolInDotNotation_IsStillRefused()
    {
        await SeedBerkshire();

        var result = await Sut().GetStockPrices("BRK.A");

        result.Should().Contain("No price series for 'BRK.A'");
        result.Should().NotContain("511.54");
    }

    [Fact]
    public async Task GetBollingerBands_ASecondarySymbol_IsRefusedLikeTheOtherPriceReads()
    {
        await SeedBerkshire();

        // Every indicator shares one resolution path, so none of them can drift back to
        // serving the primary's bars.
        var result = await Sut().GetBollingerBands("BRK-A");

        result.Should().Contain("No price series for 'BRK-A'");
    }
}

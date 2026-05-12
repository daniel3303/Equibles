using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.IntegrationTests.Helpers;
using Equibles.Yahoo.Data.Models;
using Equibles.Yahoo.Mcp.Tools;
using Equibles.Yahoo.Repositories;
using Xunit;

namespace Equibles.IntegrationTests.Mcp;

[Collection(ParadeDbCollection.Name)]
public class StockPriceToolsTests : ParadeDbMcpTestBase {
    private StockPriceTools Sut() => new(
        new DailyStockPriceRepository(DbContext),
        new CommonStockRepository(DbContext),
        ErrorManager,
        NullLogger<StockPriceTools>());

    public StockPriceToolsTests(ParadeDbFixture fixture) : base(fixture) { }

    private static CommonStock AaplStock() => new() {
        Ticker = "AAPL", Name = "Apple Inc", Cik = "0000320193",
    };

    private static CommonStock MsftStock() => new() {
        Ticker = "MSFT", Name = "Microsoft Corporation", Cik = "0000789019",
    };

    private static DailyStockPrice PriceFor(CommonStock stock, DateOnly date,
        decimal close = 150.00m, long volume = 50_000_000
    ) => new() {
        CommonStock = stock, CommonStockId = stock.Id, Date = date,
        Open = close - 1m, High = close + 1m, Low = close - 2m,
        Close = close, AdjustedClose = close, Volume = volume,
    };

    // ── GetStockPrices ───────────────────────────────────────────────────

    [Fact]
    public async Task GetStockPrices_UnknownTicker_ReturnsNotFoundMessage() {
        var result = await Sut().GetStockPrices("ZZZZ");

        result.Should().Be("Stock 'ZZZZ' not found.");
    }

    [Fact]
    public async Task GetStockPrices_StockWithoutPrices_ReturnsEmptyRangeMessage() {
        DbContext.Set<CommonStock>().Add(AaplStock());
        await DbContext.SaveChangesAsync();

        var result = await Sut().GetStockPrices("AAPL",
            startDate: "2026-04-01", endDate: "2026-04-30");

        result.Should().Contain("No price data found for AAPL");
    }

    [Fact]
    public async Task GetStockPrices_RendersOhlcvTableAscending() {
        var stock = AaplStock();
        DbContext.Set<CommonStock>().Add(stock);
        DbContext.Set<DailyStockPrice>().AddRange(
            PriceFor(stock, new DateOnly(2026, 4, 1), close: 175.50m, volume: 50_000_000),
            PriceFor(stock, new DateOnly(2026, 4, 2), close: 176.25m, volume: 45_000_000));
        await DbContext.SaveChangesAsync();

        var result = await Sut().GetStockPrices("AAPL",
            startDate: "2026-03-01", endDate: "2026-04-30");

        result.Should().Contain("Daily prices for AAPL (Apple Inc)");
        result.Should().Contain("2026-04-01");
        result.Should().Contain("175.50");
        result.Should().Contain("176.25");
        result.Should().Contain("50,000,000");
        result.IndexOf("2026-04-01").Should().BeLessThan(result.IndexOf("2026-04-02"));
    }

    [Fact]
    public async Task GetStockPrices_MaxResultsLimitsRows() {
        var stock = AaplStock();
        DbContext.Set<CommonStock>().Add(stock);
        var prices = Enumerable.Range(1, 5)
            .Select(i => PriceFor(stock, new DateOnly(2026, 4, i), close: 100m + i));
        DbContext.Set<DailyStockPrice>().AddRange(prices);
        await DbContext.SaveChangesAsync();

        var result = await Sut().GetStockPrices("AAPL",
            startDate: "2026-04-01", endDate: "2026-04-30", maxResults: 2);

        // Newest 2 retained, then re-ordered ascending → 4 and 5.
        result.Should().Contain("2026-04-04");
        result.Should().Contain("2026-04-05");
        result.Should().NotContain("2026-04-03");
    }

    [Fact]
    public async Task GetStockPrices_TrimsAndUppercasesTicker() {
        DbContext.Set<CommonStock>().Add(AaplStock());
        await DbContext.SaveChangesAsync();

        var result = await Sut().GetStockPrices("  aapl  ");

        result.Should().NotContain("not found");
    }

    // ── GetLatestPrices ──────────────────────────────────────────────────

    [Fact]
    public async Task GetLatestPrices_OverMaxTickers_ReturnsLimitMessage() {
        // GetLatestPrices runs one DB lookup per ticker — without an upper bound an agent
        // could trigger 25× amplification by passing a long ticker list. The tool short-
        // circuits at >25 with a user-facing instruction to split. Pin the limit so a
        // regression that bumps it silently can't turn this endpoint into a DB-amplification
        // DoS vector.
        var tickers = string.Join(",", Enumerable.Range(1, 26).Select(i => $"T{i:D3}"));

        var result = await Sut().GetLatestPrices(tickers);

        result.Should().Be("Maximum 25 tickers per request. Please split into multiple calls.");
    }

    [Fact]
    public async Task GetLatestPrices_OnlyCommasAndWhitespace_ReturnsNoTickersMessage() {
        // GetLatestPrices splits the comma-separated input with RemoveEmptyEntries
        // and TrimEntries, so a string of bare separators (",, , ,") collapses to
        // an empty list. The guard that catches this returns "No tickers provided."
        // — without it, the next branch would try to enforce the 25-ticker cap on
        // an empty list (passes silently) and then emit a header-only Markdown
        // table back to the MCP client, masking the input bug.
        var result = await Sut().GetLatestPrices(",, , ,");

        result.Should().Be("No tickers provided.");
    }

    [Fact]
    public async Task GetLatestPrices_KnownTickers_ReturnsLatestRow() {
        var aapl = AaplStock();
        var msft = MsftStock();
        DbContext.Set<CommonStock>().AddRange(aapl, msft);
        DbContext.Set<DailyStockPrice>().AddRange(
            PriceFor(aapl, new DateOnly(2026, 4, 1), close: 100m),
            // Latest AAPL price — should win.
            PriceFor(aapl, new DateOnly(2026, 4, 5), close: 175.50m, volume: 50_000_000),
            PriceFor(msft, new DateOnly(2026, 4, 5), close: 425.75m, volume: 22_000_000));
        await DbContext.SaveChangesAsync();

        var result = await Sut().GetLatestPrices("AAPL,MSFT");

        result.Should().Contain("AAPL");
        result.Should().Contain("MSFT");
        result.Should().Contain("175.50");
        result.Should().Contain("425.75");
        result.Should().Contain("2026-04-05");
        result.Should().NotContain("100.00"); // The older price must not show up.
    }

    [Fact]
    public async Task GetLatestPrices_UnknownTickerInList_RendersNotFoundRow() {
        var aapl = AaplStock();
        DbContext.Set<CommonStock>().Add(aapl);
        DbContext.Set<DailyStockPrice>().Add(PriceFor(aapl, new DateOnly(2026, 4, 1)));
        await DbContext.SaveChangesAsync();

        var result = await Sut().GetLatestPrices("AAPL,ZZZZ");

        result.Should().Contain("| ZZZZ | — | Not found | — |");
    }

    [Fact]
    public async Task GetLatestPrices_StockWithoutPrices_RendersNoDataRow() {
        DbContext.Set<CommonStock>().Add(AaplStock());
        await DbContext.SaveChangesAsync();

        var result = await Sut().GetLatestPrices("AAPL");

        result.Should().Contain("| AAPL | — | No data | — |");
    }

    [Fact]
    public async Task GetLatestPrices_DeduplicatesTickers() {
        var aapl = AaplStock();
        DbContext.Set<CommonStock>().Add(aapl);
        DbContext.Set<DailyStockPrice>().Add(PriceFor(aapl, new DateOnly(2026, 4, 5), close: 175.50m));
        await DbContext.SaveChangesAsync();

        var result = await Sut().GetLatestPrices("AAPL,aapl,AAPL");

        // One header line + one data line containing 175.50 — not three duplicates.
        var occurrences = result.Split("175.50").Length - 1;
        occurrences.Should().Be(1);
    }
}

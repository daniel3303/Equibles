using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.IntegrationTests.Helpers;
using Equibles.Yahoo.Data.Models;
using Equibles.Yahoo.Mcp.Tools;
using Equibles.Yahoo.Repositories;
using Xunit;

namespace Equibles.IntegrationTests.Mcp;

/// <summary>
/// Pins the MCP-audit fixes on the Yahoo price tools: strict yyyy-MM-dd date parsing
/// (no silent fallback to the default window), inverted-range errors, newest-kept
/// truncation notes, the maxResults clamp on the indicator tools, the dot→dash
/// class-share ticker fold, the day-over-day change columns on GetLatestPrices, the
/// pre-startDate warm-up look-back on windowed indicators, and the OBV anchor footnote.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class StockPriceToolsStrictArgsAndWarmupTests : ParadeDbMcpTestBase
{
    private StockPriceTools Sut() =>
        new(
            new DailyStockPriceRepository(DbContext),
            new CommonStockRepository(DbContext),
            ErrorManager,
            NullLogger<StockPriceTools>()
        );

    public StockPriceToolsStrictArgsAndWarmupTests(ParadeDbFixture fixture)
        : base(fixture) { }

    private static CommonStock Stock(string ticker = "AAPL", string name = "Apple Inc") =>
        new()
        {
            Ticker = ticker,
            Name = name,
            Cik = "0000320193",
        };

    private static DailyStockPrice PriceFor(
        CommonStock stock,
        DateOnly date,
        decimal close = 150.00m,
        long volume = 50_000_000
    ) =>
        new()
        {
            CommonStock = stock,
            CommonStockId = stock.Id,
            Date = date,
            Open = close - 1m,
            High = close + 1m,
            Low = close - 2m,
            Close = close,
            AdjustedClose = close,
            Volume = volume,
        };

    private async Task<CommonStock> SeedDailyCloses(DateOnly firstDate, params decimal[] closes)
    {
        var stock = Stock();
        DbContext.Set<CommonStock>().Add(stock);
        for (var i = 0; i < closes.Length; i++)
            DbContext.Set<DailyStockPrice>().Add(PriceFor(stock, firstDate.AddDays(i), closes[i]));
        await DbContext.SaveChangesAsync();
        return stock;
    }

    // ── Strict date arguments ────────────────────────────────────────────

    [Fact]
    public async Task GetStockPrices_MalformedStartDate_ReturnsInvalidArgumentError()
    {
        // The old shared ParseDateOr silently replaced "07/15/2026" with the default
        // 1-year window and returned a plausible table for a range the caller never
        // asked for. A malformed date must be an explicit, self-correctable error.
        DbContext.Set<CommonStock>().Add(Stock());
        await DbContext.SaveChangesAsync();

        var result = await Sut().GetStockPrices("AAPL", startDate: "07/15/2026");

        result.Should().Be("Unknown startDate '07/15/2026'. Accepted: yyyy-MM-dd.");
    }

    [Fact]
    public async Task GetStockPrices_InvertedRange_ReturnsSwapErrorNotEmptyRange()
    {
        // startDate > endDate used to fall through to the query and come back as
        // "No price data found ..." — indistinguishable from a real data gap.
        DbContext.Set<CommonStock>().Add(Stock());
        await DbContext.SaveChangesAsync();

        var result = await Sut()
            .GetStockPrices("AAPL", startDate: "2026-06-10", endDate: "2026-06-01");

        result
            .Should()
            .Be("startDate (2026-06-10) is after endDate (2026-06-01) - swap the dates.");
    }

    [Fact]
    public async Task GetOnBalanceVolume_MalformedEndDate_ReturnsInvalidArgumentError()
    {
        // The same strict parsing must guard the indicator tools' shared window loader.
        DbContext.Set<CommonStock>().Add(Stock());
        await DbContext.SaveChangesAsync();

        var result = await Sut().GetOnBalanceVolume("AAPL", endDate: "last week");

        result.Should().Be("Unknown endDate 'last week'. Accepted: yyyy-MM-dd.");
    }

    // ── Truncation signposting ───────────────────────────────────────────

    [Fact]
    public async Task GetStockPrices_RangeExceedsMaxResults_AppendsNewestKeptNote()
    {
        // The table keeps the newest rows but renders oldest→newest, so the note must
        // say "the newest N of M" — the shared "Showing first N" wording would point
        // at the wrong end of the table.
        await SeedDailyCloses(new DateOnly(2026, 4, 1), 101m, 102m, 103m, 104m, 105m);

        var result = await Sut()
            .GetStockPrices("AAPL", startDate: "2026-04-01", endDate: "2026-04-30", maxResults: 2);

        result.Should().Contain("Showing the newest 2 of 5 records");
        result.Should().Contain("2026-04-05");
        result.Should().NotContain("2026-04-03");
    }

    [Fact]
    public async Task GetStockPrices_AllRowsShown_OmitsTruncationNote()
    {
        await SeedDailyCloses(new DateOnly(2026, 4, 1), 101m, 102m);

        var result = await Sut()
            .GetStockPrices("AAPL", startDate: "2026-04-01", endDate: "2026-04-30");

        result.Should().NotContain("Showing the newest");
    }

    [Fact]
    public async Task GetOnBalanceVolume_MoreRowsThanMaxResults_AppendsTruncationNote()
    {
        // Indicator tables render newest first, so the shared "Showing first N of M"
        // wording is accurate there.
        await SeedDailyCloses(new DateOnly(2026, 4, 1), 101m, 102m, 103m);

        var result = await Sut()
            .GetOnBalanceVolume(
                "AAPL",
                startDate: "2026-04-01",
                endDate: "2026-04-30",
                maxResults: 2
            );

        result.Should().Contain("Showing first 2 of 3 results");
    }

    // ── maxResults clamp on indicator tools ──────────────────────────────

    [Fact]
    public async Task GetAverageTrueRange_NonPositiveMaxResults_StillRendersNewestRow()
    {
        // maxResults used to bypass McpLimit.Clamp on the indicator tools: an explicit
        // 0 rendered a header-only table with no rows and no message — a false empty
        // claim for a stock that has data. The clamp floors it at one row.
        await SeedDailyCloses(new DateOnly(2026, 4, 1), 101m, 102m, 103m);

        var result = await Sut()
            .GetAverageTrueRange(
                "AAPL",
                startDate: "2026-04-01",
                endDate: "2026-04-30",
                maxResults: 0
            );

        result.Should().Contain("2026-04-03");
        result.Should().NotContain("2026-04-02");
    }

    // ── Warm-up look-back before startDate ───────────────────────────────

    [Fact]
    public async Task GetBollingerBands_ExplicitStartDate_ComputesBandsFromPreRangeHistory()
    {
        // The DB holds 25 bars before the requested range, and the SMA window is 20 —
        // so every in-range row is computable from the pre-startDate warm-up fetch.
        // Before the fix the loader started exactly at startDate and the whole range
        // rendered as warm-up dashes despite ample prior history.
        var closes = Enumerable.Range(0, 30).Select(i => 100m + i % 7).ToArray();
        await SeedDailyCloses(new DateOnly(2026, 4, 1), closes);

        var result = await Sut()
            .GetBollingerBands("AAPL", startDate: "2026-04-26", endDate: "2026-04-30", period: 20);

        result.Should().Contain("2026-04-26");
        // No warm-up dash cells: every in-range row is computable (footnote text quotes
        // the dash character, so assert on the cell shape, not the bare character).
        result.Should().NotContain("| — |");
        // Pre-range warm-up bars must not leak into the rendered table.
        result.Should().NotContain("2026-04-25");
    }

    [Fact]
    public async Task GetStochasticOscillator_ExplicitStartDate_ComputesThroughRangeLeftEdge()
    {
        var closes = Enumerable.Range(0, 20).Select(i => 100m + i % 5).ToArray();
        await SeedDailyCloses(new DateOnly(2026, 4, 1), closes);

        var result = await Sut()
            .GetStochasticOscillator(
                "AAPL",
                startDate: "2026-04-16",
                endDate: "2026-04-20",
                kPeriod: 14,
                dPeriod: 3
            );

        result.Should().Contain("2026-04-16");
        result.Should().NotContain("| — |");
        result.Should().NotContain("2026-04-15");
    }

    [Fact]
    public async Task GetAverageTrueRange_NoPriorHistory_StillMarksWarmupRowsWithDash()
    {
        // With no bars before the range the warm-up fetch finds nothing and the first
        // period-1 rows legitimately stay dashes, explained by the footnote.
        await SeedDailyCloses(new DateOnly(2026, 4, 1), 101m, 102m, 103m);

        var result = await Sut()
            .GetAverageTrueRange(
                "AAPL",
                startDate: "2026-04-01",
                endDate: "2026-04-30",
                period: 14
            );

        result.Should().Contain("| — |");
        result.Should().Contain("too little prior price history");
    }

    // ── OBV anchor semantics ─────────────────────────────────────────────

    [Fact]
    public async Task GetOnBalanceVolume_FootnoteNamesTheZeroAnchorDate()
    {
        // OBV is window-relative: absolute values shift with startDate. The footnote
        // must name the anchor bar so the level stays interpretable even when
        // maxResults truncates the zero-anchor row out of the table.
        await SeedDailyCloses(new DateOnly(2026, 4, 1), 101m, 102m, 103m);

        var result = await Sut()
            .GetOnBalanceVolume("AAPL", startDate: "2026-04-01", endDate: "2026-04-30");

        result.Should().Contain("OBV is anchored at 0 on 2026-04-01");
    }

    // ── Dot class-share notation ─────────────────────────────────────────

    [Fact]
    public async Task GetLatestPrices_DotClassTicker_ResolvesDashStoredForm()
    {
        // Price data stores class shares in the Yahoo dash form (BRK-B). The dot form
        // (BRK.B) is the same symbol in a different notation — a mechanical format
        // conversion, so the lookup folds '.' to '-' on a miss instead of reporting
        // the stock as not found.
        var brk = Stock(ticker: "BRK-B", name: "Berkshire Hathaway Inc");
        DbContext.Set<CommonStock>().Add(brk);
        DbContext
            .Set<DailyStockPrice>()
            .Add(PriceFor(brk, new DateOnly(2026, 4, 5), close: 412.34m));
        await DbContext.SaveChangesAsync();

        var result = await Sut().GetLatestPrices("BRK.B");

        result.Should().Contain("412.34");
        result.Should().NotContain("Not found");
    }

    [Fact]
    public async Task GetStockPrices_DotClassTicker_ResolvesDashStoredForm()
    {
        var brk = Stock(ticker: "BRK-B", name: "Berkshire Hathaway Inc");
        DbContext.Set<CommonStock>().Add(brk);
        DbContext
            .Set<DailyStockPrice>()
            .Add(PriceFor(brk, new DateOnly(2026, 4, 5), close: 412.34m));
        await DbContext.SaveChangesAsync();

        var result = await Sut().GetStockPrices("BRK.B");

        result.Should().Contain("Daily prices for BRK-B");
        result.Should().Contain("412.34");
    }

    // ── GetLatestPrices change columns ───────────────────────────────────

    [Fact]
    public async Task GetLatestPrices_TwoBars_RendersDayOverDayChange()
    {
        await SeedDailyCloses(new DateOnly(2026, 4, 1), 100m, 110m);

        var result = await Sut().GetLatestPrices("AAPL");

        result.Should().Contain("| +10.00 |");
        result.Should().Contain("| +10.00% |");
        result.Should().Contain("2026-04-02");
    }

    [Fact]
    public async Task GetLatestPrices_SingleBar_RendersDashChangeCells()
    {
        // With no prior close the change columns must degrade to dashes, not crash
        // or fabricate a zero change.
        await SeedDailyCloses(new DateOnly(2026, 4, 1), 100m);

        var result = await Sut().GetLatestPrices("AAPL");

        result.Should().Contain("| 100.00 | — | — |");
    }
}

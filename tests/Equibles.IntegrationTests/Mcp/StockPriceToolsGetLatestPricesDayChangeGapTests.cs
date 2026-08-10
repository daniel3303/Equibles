using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.IntegrationTests.Helpers;
using Equibles.Yahoo.Data.Models;
using Equibles.Yahoo.Mcp.Tools;
using Equibles.Yahoo.Repositories;
using Xunit;

namespace Equibles.IntegrationTests.Mcp;

/// <summary>
/// Contract: the Change / Change % columns state a ONE-SESSION move, so they are rendered only
/// when the stored series holds the trading day immediately before the row's date.
///
/// The end-of-day price lane crawls the whole common-stock universe and can finish a session or
/// more behind, so the second-newest stored row is routinely two or more sessions back. Reading
/// it as "yesterday" turns a multi-session move into a day change — measured in production, a
/// symbol whose two newest rows sat 25 sessions apart reported +11,630.77% as its day change.
///
/// Pins the rendered output rather than the guard alone: the columns must blank AND the footnote
/// must appear, so a caller cannot read the em-dash as missing coverage.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class StockPriceToolsGetLatestPricesDayChangeGapTests : ParadeDbMcpTestBase
{
    public StockPriceToolsGetLatestPricesDayChangeGapTests(ParadeDbFixture fixture)
        : base(fixture) { }

    private StockPriceTools Sut() =>
        new(
            new DailyStockPriceRepository(DbContext),
            new CommonStockRepository(DbContext),
            new Equibles.CorporateActions.Repositories.StockSplitRepository(DbContext),
            ErrorManager,
            NullLogger<StockPriceTools>()
        );

    private async Task Seed(string ticker, params (DateOnly Date, decimal Close)[] bars)
    {
        var stock = new CommonStock
        {
            Ticker = ticker,
            Name = $"{ticker} Inc",
            Cik = ticker.PadLeft(10, '0'),
        };
        DbContext.Set<CommonStock>().Add(stock);
        await DbContext.SaveChangesAsync();

        foreach (var (date, close) in bars)
        {
            DbContext
                .Set<DailyStockPrice>()
                .Add(
                    new DailyStockPrice
                    {
                        CommonStockId = stock.Id,
                        Date = date,
                        Open = close,
                        High = close,
                        Low = close,
                        Close = close,
                        AdjustedClose = close,
                        Volume = 1_000_000,
                    }
                );
        }
        await DbContext.SaveChangesAsync();
    }

    [Fact]
    public async Task GetLatestPrices_ConsecutiveSessions_RendersTheChange()
    {
        // Thu 2026-07-30 -> Fri 2026-07-31, adjacent sessions: a real +10% day.
        await Seed("ADJ", (new DateOnly(2026, 7, 30), 100m), (new DateOnly(2026, 7, 31), 110m));

        var result = await Sut().GetLatestPrices("ADJ");

        result.Should().Contain("+10.00").And.Contain("+10.00%");
        result.Should().NotContain("no row for the session before");
    }

    [Fact]
    public async Task GetLatestPrices_SessionSkipped_BlanksTheChangeAndExplains()
    {
        // Mon 2026-07-27 over Thu 2026-07-23 with Friday missing — the reported failure. Four
        // calendar days apart, so no day-count tolerance can tell it from the holiday case below.
        await Seed("GAP", (new DateOnly(2026, 7, 23), 100m), (new DateOnly(2026, 7, 27), 110m));

        var result = await Sut().GetLatestPrices("GAP");

        // The +10% multi-session move may appear in the 52-week range columns — it IS the
        // window's span — but never in the Change cells, which stay em-dashed. Pinning the
        // full row keeps the two placements distinguishable.
        result
            .Should()
            .Contain(
                "| GAP | 2026-07-27 | 110.00 | — | — | 1,000,000 | 110.00\\* | 100.00\\* | 0.00% | +10.00% |"
            );
        result.Should().Contain("no row for the session before");
    }

    [Fact]
    public async Task GetLatestPrices_GapOverAMarketHoliday_StillRendersTheChange()
    {
        // Thu 2026-07-02 -> Mon 2026-07-06: also four calendar days, but adjacent sessions
        // because Jul 4 2026 falls on a Saturday and the NYSE observes it on Fri Jul 3. The
        // trading calendar is the only thing that separates this from the case above.
        await Seed("HOL", (new DateOnly(2026, 7, 2), 100m), (new DateOnly(2026, 7, 6), 110m));

        var result = await Sut().GetLatestPrices("HOL");

        result.Should().Contain("+10.00%");
        result.Should().NotContain("no row for the session before");
    }

    [Fact]
    public async Task GetLatestPrices_PriorBarOnAnNyseClosure_StillRendersTheChange()
    {
        // Fri 2026-07-03 is the observed Independence Day close, yet 297 securities in production
        // carry a bar for it — foreign ordinaries quoted here trade on their home calendar. No
        // NYSE session sits between the two bars, so this is a real one-session move.
        await Seed("FGN", (new DateOnly(2026, 7, 3), 100m), (new DateOnly(2026, 7, 6), 110m));

        var result = await Sut().GetLatestPrices("FGN");

        result.Should().Contain("+10.00%");
        result.Should().NotContain("no row for the session before");
    }

    [Fact]
    public async Task GetLatestPrices_SingleRow_BlanksTheChangeWithoutTheFootnote()
    {
        // One stored bar is not a gap — there is no prior row to have skipped a session, so the
        // footnote would misdescribe it.
        await Seed("ONE", (new DateOnly(2026, 7, 31), 110m));

        var result = await Sut().GetLatestPrices("ONE");

        result.Should().Contain("| ONE | 2026-07-31 | 110.00 | — | — |");
        result.Should().NotContain("no row for the session before");
    }
}

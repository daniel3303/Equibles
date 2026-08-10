using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.IntegrationTests.Helpers;
using Equibles.Yahoo.Data.Models;
using Equibles.Yahoo.Mcp.Tools;
using Equibles.Yahoo.Repositories;
using Xunit;

namespace Equibles.IntegrationTests.Mcp;

/// <summary>
/// Pins the adjusted-close rule on GetStockPrices (#7058).
/// <para>
/// AdjustedClose is an auxiliary stored provider series that can be rewritten with the full price
/// history and is not guaranteed to be complete total return. Neither equality nor a difference
/// identifies which corporate actions it reflects, and the tool makes no universal Close-basis
/// claim.
/// </para>
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class StockPriceToolsAdjustedCloseTests : ParadeDbMcpTestBase
{
    public StockPriceToolsAdjustedCloseTests(ParadeDbFixture fixture)
        : base(fixture) { }

    private StockPriceTools Sut() =>
        new(
            new DailyStockPriceRepository(DbContext),
            new CommonStockRepository(DbContext),
            ErrorManager,
            NullLogger<StockPriceTools>()
        );

    [Fact]
    public async Task GetStockPrices_WhenTheWindowHoldsAnAdjustment_RendersTheAdjustedClose()
    {
        var stock = await SeedPrices(adjustedOffset: -3m);

        var result = await Sut()
            .GetStockPrices(stock.Ticker, startDate: "2025-01-06", endDate: "2025-01-20");

        result.Should().Contain("| Date | Open | High | Low | Close | Adj Close | Volume |");
        result.Should().Contain("97.00");
        result.Should().Contain("auxiliary stored provider series");
        result.Should().Contain("rewritten with the full history during split reconciliation");
        result.Should().Contain("not guaranteed to be complete total return");
        result.Should().NotContain("compute total return from Adj Close");
        result.Should().NotContain("splits only");
        result.Should().NotContain("dividends never restate");
        result.Should().NotContain("price as traded");
        result.Should().NotContain("never restated");
    }

    [Fact]
    public async Task GetStockPrices_WhenNothingWasAdjusted_OmitsTheColumnAndSaysWhy()
    {
        // This stored window has an identical adjusted series, so the column would repeat Close
        // on every row. The note must describe only that stored equality, not infer why it exists.
        var stock = await SeedPrices(adjustedOffset: 0m);

        var result = await Sut()
            .GetStockPrices(stock.Ticker, startDate: "2025-01-06", endDate: "2025-01-20");

        result.Should().Contain("| Date | Open | High | Low | Close | Volume |");
        result.Should().NotContain("| Date | Open | High | Low | Close | Adj Close | Volume |");
        // Says what is true of the stored rows, and nothing about whether the issuer acted.
        result.Should().Contain("equals Close on every row shown");
        result.Should().Contain("rewritten with the full history during split reconciliation");
        result.Should().NotContain("no split or dividend");
        result.Should().NotContain("total return equals price return");
    }

    [Fact]
    public async Task GetStockPrices_ForADividendPayerWithNoStoredRebase_MakesNoCorporateActionClaim()
    {
        // The production case that made the old wording a false statement (issue #7088): each
        // dividend payer's differing bars stop on the last session before its newest ex-date —
        // KO 2026-06-12 vs ex-div 2026-06-15, PG 2026-07-23 vs 2026-07-24 — so the newest rows
        // match while the company plainly paid. The tool must not translate "our two stored
        // columns match" into "the company paid nothing".
        var stock = await SeedPrices(adjustedOffset: 0m);

        var result = await Sut()
            .GetStockPrices(stock.Ticker, startDate: "2025-01-06", endDate: "2025-01-20");

        foreach (
            var forbidden in new[]
            {
                "no split or dividend",
                "no dividend",
                "total return equals price return",
                "adjusted for splits and dividends",
                "splits only",
                "not dividend-adjusted",
            }
        )
        {
            result.Should().NotContain(forbidden);
        }
    }

    private async Task<CommonStock> SeedPrices(decimal adjustedOffset)
    {
        var stock = MakeStock();
        DbContext.Set<CommonStock>().Add(stock);
        await DbContext.SaveChangesAsync();

        var start = new DateOnly(2025, 1, 6);
        for (var i = 0; i < 10; i++)
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
                        AdjustedClose = 100m + adjustedOffset,
                        Volume = 1_000,
                    }
                );
        }
        await DbContext.SaveChangesAsync();
        return stock;
    }

    private static CommonStock MakeStock() =>
        new()
        {
            Ticker = "AAPL",
            Name = "Apple Inc",
            Cik = "0000320193",
        };
}

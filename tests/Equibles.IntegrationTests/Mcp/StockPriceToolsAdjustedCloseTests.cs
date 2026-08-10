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
/// The rule is about STORED ROWS, not about the world. Our AdjustedClose is restated only when a
/// split rebase runs; dividends never rebase it and the forward EOD lane writes AdjustedClose =
/// Close. So equality between the two columns proves only that no split rebase has touched these
/// rows — a dividend payer's recent bars are equal while dividends were certainly paid. The tool
/// must therefore never infer an absence of corporate actions, and must never offer the series as
/// a total-return basis.
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
        result.Should().Contain("split-adjusted");
        // The series is split-only, so promising total return from it would be wrong in the
        // understating direction — the very error #7058 was filed about.
        result.Should().NotContain("total return from Adj Close");
        result.Should().Contain("NOT maintained for dividends");
    }

    [Fact]
    public async Task GetStockPrices_WhenNothingWasAdjusted_OmitsTheColumnAndSaysWhy()
    {
        // A stock that neither split nor paid a dividend has an identical adjusted series, so the
        // column would repeat the close on every row. Dropping it silently would read as the tool
        // being unable to answer total return, hence the explicit statement.
        var stock = await SeedPrices(adjustedOffset: 0m);

        var result = await Sut()
            .GetStockPrices(stock.Ticker, startDate: "2025-01-06", endDate: "2025-01-20");

        result.Should().Contain("| Date | Open | High | Low | Close | Volume |");
        result.Should().NotContain("Adj Close");
        // Says what is true of the stored rows, and nothing about whether the issuer acted.
        result.Should().Contain("equals Close on every row shown");
        result.Should().NotContain("no split or dividend");
        result.Should().NotContain("total return equals price return");
    }

    [Fact]
    public async Task GetStockPrices_ForADividendPayerWithNoStoredRebase_MakesNoCorporateActionClaim()
    {
        // The production case that made the old wording a false statement: KO, JNJ, MSFT and PG
        // all paid dividends recently, yet none has a single adj != close bar since 2026-05-01,
        // because only a split rebase restates the stored series. The tool must not translate
        // "our two stored columns match" into "the company paid nothing".
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

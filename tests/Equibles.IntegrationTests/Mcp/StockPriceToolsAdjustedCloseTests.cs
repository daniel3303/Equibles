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
/// The rule is about STORED ROWS, not about the world. A stored AdjustedClose carries whatever
/// adjustment the provider had applied when that row was written, and nothing restates it when a
/// LATER corporate action goes ex; the forward EOD lane also writes AdjustedClose = Close. So one
/// series straddles bases — a dividend payer's older bars are discounted and its newer bars are
/// not — and equality between the two columns proves nothing about the issuer. The tool must
/// never infer an absence of corporate actions, must never offer the series as a total-return
/// basis, and must never describe it as split-only: a caller told that would add dividends back
/// itself and double-discount the pre-seam rows.
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
        result.Should().Contain("stored adjusted close");
        result.Should().Contain("never restated when a later corporate action goes ex");
        // Promising total return off an inconsistent basis is the error #7058 was filed about;
        // calling the series split-only is the opposite error, because a caller who believed it
        // would add dividends back and double-discount every pre-seam row.
        result.Should().NotContain("total return from Adj Close");
        result.Should().NotContain("splits only");
        result.Should().NotContain("dividends never restate");
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

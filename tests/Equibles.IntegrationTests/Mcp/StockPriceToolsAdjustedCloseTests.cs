using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.IntegrationTests.Helpers;
using Equibles.Yahoo.Data.Models;
using Equibles.Yahoo.Mcp.Tools;
using Equibles.Yahoo.Repositories;
using Xunit;

namespace Equibles.IntegrationTests.Mcp;

/// <summary>
/// Pins the adjusted-close rule on GetStockPrices (#7058/#7088).
/// <para>
/// Captured splits and cash dividends trigger a full-series provider-history refresh, but the
/// stored rows do not certify which split basis the provider returned. The tool exposes the
/// provider-adjusted values without promising a universally consistent total-return window.
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
            new Equibles.CorporateActions.Repositories.StockSplitRepository(DbContext),
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
        result.Should().Contain("provider-adjusted close");
        result.Should().Contain("full-history refresh");
        result.Should().Contain("stored rows do not certify which split basis");
        result.Should().Contain("Do not infer a consistent total-return window");
        result.Should().NotContain("Once pending actions reconcile");
    }

    [Fact]
    public async Task GetStockPrices_WhenNothingWasAdjusted_OmitsTheColumnAndSaysWhy()
    {
        // This stored window has an identical adjusted series, so the column would repeat Close.
        var stock = await SeedPrices(adjustedOffset: 0m);

        var result = await Sut()
            .GetStockPrices(stock.Ticker, startDate: "2025-01-06", endDate: "2025-01-20");

        result.Should().Contain("| Date | Open | High | Low | Close | Volume |");
        result.Should().NotContain("| Date | Open | High | Low | Close | Adj Close | Volume |");
        result.Should().Contain("Adj Close equals Close on every row shown");
        result.Should().Contain("splits and cash dividends trigger a full-history refresh");
        result
            .Should()
            .Contain("equality does not prove that a split-spanning window uses one basis");
    }

    [Fact]
    public async Task GetStockPrices_EqualAdjustedClose_DoesNotCertifySplitBasis()
    {
        var stock = await SeedPrices(adjustedOffset: 0m);

        var result = await Sut()
            .GetStockPrices(stock.Ticker, startDate: "2025-01-06", endDate: "2025-01-20");

        result
            .Should()
            .Contain("equality does not prove that a split-spanning window uses one basis");
        result.Should().NotContain("Once pending actions reconcile");
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

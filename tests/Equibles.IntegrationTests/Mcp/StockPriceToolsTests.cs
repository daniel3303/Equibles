using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Repositories;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data;
using Equibles.Errors.Repositories;
using Equibles.Yahoo.Data;
using Equibles.Yahoo.Mcp.Tools;
using Equibles.Yahoo.Repositories;
using Equibles.IntegrationTests.Helpers;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.IntegrationTests.Mcp;

public class StockPriceToolsTests {
    [Fact]
    public async Task GetLatestPrices_OverMaxTickers_ReturnsLimitMessage() {
        // GetLatestPrices runs one DB lookup per ticker — without an upper bound an
        // agent could trigger a 25× amplification by passing a long ticker list. The
        // tool short-circuits at >25 with a user-facing instruction to split. Pin
        // the limit so a regression that removes it (or bumps it silently) can't
        // turn this endpoint into a database-amplification DoS vector.
        using var ctx = TestDbContextFactory.Create(
            new CommonStocksModuleConfiguration(),
            new YahooModuleConfiguration(),
            new ErrorsModuleConfiguration());
        var stockRepo = new CommonStockRepository(ctx);
        var priceRepo = new DailyStockPriceRepository(ctx);
        var errorManager = new ErrorManager(new ErrorRepository(ctx));
        var sut = new StockPriceTools(priceRepo, stockRepo, errorManager, Substitute.For<ILogger<StockPriceTools>>());

        var tickers = string.Join(",", Enumerable.Range(1, 26).Select(i => $"T{i:D3}"));
        var result = await sut.GetLatestPrices(tickers);

        result.Should().Be("Maximum 25 tickers per request. Please split into multiple calls.");
    }

    [Fact]
    public async Task GetLatestPrices_OnlyCommasAndWhitespace_ReturnsNoTickersMessage() {
        // GetLatestPrices splits the comma-separated input with RemoveEmptyEntries
        // and TrimEntries, so a string of bare separators (",, , ,") collapses to
        // an empty list. The guard that catches this returns "No tickers provided."
        // — without it, the next branch would try to enforce the 25-ticker cap on
        // an empty list (passes silently) and then emit a header-only Markdown
        // table back to the MCP client, masking the input bug. Pin the
        // explicit no-tickers reply so the agent gets a clear signal.
        using var ctx = TestDbContextFactory.Create(
            new CommonStocksModuleConfiguration(),
            new YahooModuleConfiguration(),
            new ErrorsModuleConfiguration());
        var stockRepo = new CommonStockRepository(ctx);
        var priceRepo = new DailyStockPriceRepository(ctx);
        var errorManager = new ErrorManager(new ErrorRepository(ctx));
        var sut = new StockPriceTools(priceRepo, stockRepo, errorManager, Substitute.For<ILogger<StockPriceTools>>());

        var result = await sut.GetLatestPrices(",, , ,");

        result.Should().Be("No tickers provided.");
    }
}

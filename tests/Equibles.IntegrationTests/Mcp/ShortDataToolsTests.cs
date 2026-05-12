using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Repositories;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data;
using Equibles.Errors.Repositories;
using Equibles.Finra.Data;
using Equibles.Finra.Mcp.Tools;
using Equibles.Finra.Repositories;
using Equibles.IntegrationTests.Helpers;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.IntegrationTests.Mcp;

public class ShortDataToolsTests {
    [Fact]
    public async Task GetShortVolume_UnknownTicker_ReturnsStockNotFoundMessage() {
        // ShortDataTools must short-circuit when the ticker doesn't match any stock —
        // otherwise the subsequent `GetHistoryByStock(null)` call dereferences the
        // missing entity and the tool returns the generic McpToolExecutor error
        // string, masking the real (user-facing) "stock not found" feedback. Pinning
        // the early-return path keeps the MCP client message specific.
        using var ctx = TestDbContextFactory.Create(
            new CommonStocksModuleConfiguration(),
            new FinraModuleConfiguration(),
            new ErrorsModuleConfiguration());
        var stockRepo = new CommonStockRepository(ctx);
        var shortVolumeRepo = new DailyShortVolumeRepository(ctx);
        var shortInterestRepo = new ShortInterestRepository(ctx);
        var errorManager = new ErrorManager(new ErrorRepository(ctx));
        var sut = new ShortDataTools(shortVolumeRepo, shortInterestRepo, stockRepo, errorManager, Substitute.For<ILogger<ShortDataTools>>());

        var result = await sut.GetShortVolume("ZZZZ");

        result.Should().Be("Stock 'ZZZZ' not found.");
    }

    [Fact]
    public async Task GetShortInterestSnapshot_EmptyDatabase_ReturnsNoDataAvailableMessage() {
        // GetShortInterestSnapshot has two distinct empty-state messages — one for an
        // entirely empty table ("No short interest data available.") and one for an
        // empty filtered result inside an existing settlement period. The first message
        // matters because it tells an MCP client the FINRA scraper hasn't run yet at all
        // (vs. "ran but nothing met your filter"). If a refactor consolidated both branches
        // into the filtered message, the agent would mis-diagnose a never-scraped database
        // as a too-strict filter. Pin the empty-table path.
        using var ctx = TestDbContextFactory.Create(
            new CommonStocksModuleConfiguration(),
            new FinraModuleConfiguration(),
            new ErrorsModuleConfiguration());
        var stockRepo = new CommonStockRepository(ctx);
        var shortVolumeRepo = new DailyShortVolumeRepository(ctx);
        var shortInterestRepo = new ShortInterestRepository(ctx);
        var errorManager = new ErrorManager(new ErrorRepository(ctx));
        var sut = new ShortDataTools(shortVolumeRepo, shortInterestRepo, stockRepo, errorManager, Substitute.For<ILogger<ShortDataTools>>());

        var result = await sut.GetShortInterestSnapshot();

        result.Should().Be("No short interest data available.");
    }
}

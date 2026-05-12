using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Repositories;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data;
using Equibles.Errors.Repositories;
using Equibles.Media.Data;
using Equibles.Sec.Mcp.Tools;
using Equibles.Sec.Repositories;
using Equibles.IntegrationTests.Helpers;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.IntegrationTests.Mcp;

public class FailToDeliverToolsTests {
    [Fact]
    public async Task GetFailsToDeliver_UnknownTicker_ReturnsStockNotFoundMessage() {
        // FailToDeliverTools must short-circuit when the ticker doesn't match any stock —
        // otherwise the subsequent `GetByStock(null)` call dereferences the missing entity
        // and the tool returns the generic McpToolExecutor error string, masking the real
        // (user-facing) "stock not found" feedback. Pinning the early-return path keeps the
        // MCP client message specific.
        using var ctx = TestDbContextFactory.Create(
            new CommonStocksModuleConfiguration(),
            new MediaModuleConfiguration(),
            new SecTestModuleConfiguration(),
            new ErrorsModuleConfiguration());
        var stockRepo = new CommonStockRepository(ctx);
        var ftdRepo = new FailToDeliverRepository(ctx);
        var errorManager = new ErrorManager(new ErrorRepository(ctx));
        var sut = new FailToDeliverTools(ftdRepo, stockRepo, errorManager, Substitute.For<ILogger<FailToDeliverTools>>());

        var result = await sut.GetFailsToDeliver("ZZZZ");

        result.Should().Be("Stock 'ZZZZ' not found.");
    }
}

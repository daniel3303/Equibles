using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Repositories;
using Equibles.Congress.Data;
using Equibles.Congress.Mcp.Tools;
using Equibles.Congress.Repositories;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data;
using Equibles.Errors.Repositories;
using Equibles.IntegrationTests.Helpers;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.IntegrationTests.Mcp;

public class CongressToolsTests {
    [Fact]
    public async Task GetMemberTrades_UnknownMember_ReturnsNotFoundMessageWithSearchHint() {
        // The unknown-member message specifically points at SearchCongressMembers so an
        // MCP client knows how to recover when the supplied name doesn't match. If a
        // refactor drops the cross-reference (or makes it a generic "not found"), the
        // agent loses the discovery path and the user gets stuck. Pin the exact message.
        using var ctx = TestDbContextFactory.Create(
            new CommonStocksModuleConfiguration(),
            new CongressModuleConfiguration(),
            new ErrorsModuleConfiguration());
        var tradeRepo = new CongressionalTradeRepository(ctx);
        var memberRepo = new CongressMemberRepository(ctx);
        var stockRepo = new CommonStockRepository(ctx);
        var errorManager = new ErrorManager(new ErrorRepository(ctx));
        var sut = new CongressTools(tradeRepo, memberRepo, stockRepo, errorManager, Substitute.For<ILogger<CongressTools>>());

        var result = await sut.GetMemberTrades("Nonexistent Person");

        result.Should().Be("Member 'Nonexistent Person' not found. Use SearchCongressMembers to find the exact name.");
    }
}

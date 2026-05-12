using Equibles.Cftc.Data;
using Equibles.Cftc.Mcp.Tools;
using Equibles.Cftc.Repositories;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data;
using Equibles.Errors.Repositories;
using Equibles.IntegrationTests.Helpers;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.IntegrationTests.Mcp;

public class CftcToolsTests {
    [Fact]
    public async Task GetCftcPositioning_UnknownContract_ReturnsNotFoundMessageWithSearchHint() {
        // The unknown-contract message specifically points at SearchCftcMarkets so an
        // MCP client knows how to discover the right market code when the supplied one
        // doesn't match. CFTC codes are opaque (e.g., "067651" = Crude Oil) — without
        // the cross-reference, an MCP client has no path to recover from a typo.
        // Pin the exact message so a refactor can't drop the discovery hint.
        using var ctx = TestDbContextFactory.Create(
            new CftcModuleConfiguration(),
            new ErrorsModuleConfiguration());
        var contractRepo = new CftcContractRepository(ctx);
        var reportRepo = new CftcPositionReportRepository(ctx);
        var errorManager = new ErrorManager(new ErrorRepository(ctx));
        var sut = new CftcTools(contractRepo, reportRepo, errorManager, Substitute.For<ILogger<CftcTools>>());

        var result = await sut.GetCftcPositioning("999999");

        result.Should().Be("Contract '999999' not found. Use SearchCftcMarkets to find available contracts.");
    }

    [Fact]
    public async Task GetLatestCftcData_EmptyDatabase_ReturnsNoContractsMessage() {
        // GetLatestCftcData uses a different empty-state message than GetCftcPositioning
        // because it doesn't take a market code — it dumps the latest report per contract
        // across all curated contracts. When the contracts table is empty (fresh install
        // before EnsureContractsExist has run, or a misconfigured deployment), the user-
        // facing message must say "No CFTC contracts found in the database." so the agent
        // can distinguish a config problem from a "no data for this code" problem. Pin
        // the exact text so a refactor that swaps in the generic positioning message
        // can't hide a config gap behind a misleading reply.
        using var ctx = TestDbContextFactory.Create(
            new CftcModuleConfiguration(),
            new ErrorsModuleConfiguration());
        var contractRepo = new CftcContractRepository(ctx);
        var reportRepo = new CftcPositionReportRepository(ctx);
        var errorManager = new ErrorManager(new ErrorRepository(ctx));
        var sut = new CftcTools(contractRepo, reportRepo, errorManager, Substitute.For<ILogger<CftcTools>>());

        var result = await sut.GetLatestCftcData();

        result.Should().Be("No CFTC contracts found in the database.");
    }
}

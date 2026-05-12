using Equibles.Cboe.Data;
using Equibles.Cboe.Mcp.Tools;
using Equibles.Cboe.Repositories;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data;
using Equibles.Errors.Repositories;
using Equibles.IntegrationTests.Helpers;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.IntegrationTests.Mcp;

public class CboeToolsTests {
    [Fact]
    public async Task GetPutCallRatios_InvalidType_ReturnsErrorWithValidTypesList() {
        // The invalid-type message hard-codes the list of accepted enum values
        // ("Total, Equity, Index, Vix, Etp"). When CboePutCallRatioType gains a
        // new variant, this string must be updated in lockstep — without a pin
        // an MCP client sees a stale enumeration and can't discover the new
        // type. Lock the exact message so a drift forces a deliberate change.
        using var ctx = TestDbContextFactory.Create(
            new CboeModuleConfiguration(),
            new ErrorsModuleConfiguration());
        var putCallRepo = new CboePutCallRatioRepository(ctx);
        var vixRepo = new CboeVixDailyRepository(ctx);
        var errorManager = new ErrorManager(new ErrorRepository(ctx));
        var sut = new CboeTools(putCallRepo, vixRepo, errorManager, Substitute.For<ILogger<CboeTools>>());

        var result = await sut.GetPutCallRatios(type: "Bogus");

        result.Should().Be("Invalid type 'Bogus'. Valid types: Total, Equity, Index, Vix, Etp");
    }
}

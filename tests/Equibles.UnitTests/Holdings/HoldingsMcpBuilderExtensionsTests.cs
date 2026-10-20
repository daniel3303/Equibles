using Equibles.Holdings.BusinessLogic.Ownership;
using Equibles.Holdings.Mcp.Extensions;
using Equibles.Holdings.Mcp.Tools;
using Equibles.Mcp;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Equibles.UnitTests.Holdings;

public class HoldingsMcpBuilderExtensionsTests
{
    [Fact]
    public void AddHoldings_RegistersTheHoldingsModule()
    {
        // AddHoldings wires the holdings MCP tools through HoldingsMcpModule, which scans the
        // tools' own assembly and registers the unified holder list the ownership tools read.
        var services = new ServiceCollection();
        var mcpServerBuilder = Substitute.For<IMcpServerBuilder>();
        var builder = new EquiblesMcpBuilder(services, mcpServerBuilder);

        builder.AddHoldings();

        builder.Modules.Should().ContainSingle().Which.Should().BeOfType<HoldingsMcpModule>();
    }

    [Fact]
    public void RegisterTools_RegistersEachOwnershipLaneOnce()
    {
        // A host that adds the module twice (or also calls AddStockOwnership) must never count
        // a lane's rows twice, so each contributor is registered once.
        var services = new ServiceCollection();
        var mcpServerBuilder = Substitute.For<IMcpServerBuilder>();
        mcpServerBuilder.Services.Returns(services);
        var module = new HoldingsMcpModule();

        module.RegisterTools(mcpServerBuilder, services);
        module.RegisterTools(mcpServerBuilder, services);

        services
            .Where(d => d.ServiceType == typeof(IHolderSourceContributor))
            .Select(d => d.ImplementationType)
            .Should()
            .BeEquivalentTo([
                typeof(Form13FContributor),
                typeof(Schedule13DGContributor),
                typeof(InsiderContributor),
                typeof(FundContributor),
            ]);
        services.Count(d => d.ServiceType == typeof(StockOwnershipReader)).Should().Be(1);
    }
}

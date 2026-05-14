using Equibles.Holdings.Mcp.Extensions;
using Equibles.Holdings.Mcp.Tools;
using Equibles.Mcp;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Equibles.UnitTests.Holdings;

public class HoldingsMcpBuilderExtensionsTests
{
    [Fact]
    public void AddHoldings_RegistersAssemblyMcpModuleForInstitutionalHoldingsTools()
    {
        // AddHoldings wires the 13F institutional-holdings MCP tools into
        // the EquiblesMcpBuilder via AssemblyMcpModule<InstitutionalHoldingsTools>.
        // The marker type drives the AutoWiring assembly scan; a regression
        // that swaps it for a non-Holdings type would silently miss every
        // Holdings MCP tool at runtime. Pin the marker so the regression
        // surfaces here.
        var services = new ServiceCollection();
        var mcpServerBuilder = Substitute.For<IMcpServerBuilder>();
        var builder = new EquiblesMcpBuilder(services, mcpServerBuilder);

        builder.AddHoldings();

        builder
            .Modules.Should()
            .ContainSingle()
            .Which.Should()
            .BeOfType<AssemblyMcpModule<InstitutionalHoldingsTools>>();
    }
}

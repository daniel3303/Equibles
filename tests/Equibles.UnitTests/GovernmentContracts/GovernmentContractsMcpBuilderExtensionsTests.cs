using Equibles.GovernmentContracts.Mcp.Extensions;
using Equibles.GovernmentContracts.Mcp.Tools;
using Equibles.Mcp;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Equibles.UnitTests.GovernmentContracts;

public class GovernmentContractsMcpBuilderExtensionsTests
{
    [Fact]
    public void AddGovernmentContracts_RegistersAssemblyMcpModuleForGovernmentContractsTools()
    {
        // AddGovernmentContracts wires the USAspending award MCP tools into the
        // EquiblesMcpBuilder via AssemblyMcpModule<GovernmentContractsTools>. The
        // marker type drives the AutoWiring assembly scan; a regression that swaps
        // it for a non-GovernmentContracts type would silently miss every award
        // tool at runtime. Pin the marker so the regression surfaces here.
        var services = new ServiceCollection();
        var mcpServerBuilder = Substitute.For<IMcpServerBuilder>();
        var builder = new EquiblesMcpBuilder(services, mcpServerBuilder);

        builder.AddGovernmentContracts();

        builder
            .Modules.Should()
            .ContainSingle()
            .Which.Should()
            .BeOfType<AssemblyMcpModule<GovernmentContractsTools>>();
    }
}

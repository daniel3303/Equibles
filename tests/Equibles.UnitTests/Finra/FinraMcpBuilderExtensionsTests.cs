using Equibles.Finra.Mcp.Extensions;
using Equibles.Finra.Mcp.Tools;
using Equibles.Mcp;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Equibles.UnitTests.Finra;

public class FinraMcpBuilderExtensionsTests
{
    [Fact]
    public void AddShortData_RegistersAssemblyMcpModuleForShortDataTools()
    {
        // AddShortData wires the FINRA short-volume / short-interest MCP
        // tools into the EquiblesMcpBuilder via
        // AssemblyMcpModule<ShortDataTools>. The marker type drives the
        // AutoWiring assembly scan; a regression that swaps it for a
        // non-Finra type would silently miss every short-data MCP tool
        // at runtime. Pin the marker so the regression surfaces here.
        var services = new ServiceCollection();
        var mcpServerBuilder = Substitute.For<IMcpServerBuilder>();
        var builder = new EquiblesMcpBuilder(services, mcpServerBuilder);

        builder.AddShortData();

        builder
            .Modules.Should()
            .ContainSingle()
            .Which.Should()
            .BeOfType<AssemblyMcpModule<ShortDataTools>>();
    }
}

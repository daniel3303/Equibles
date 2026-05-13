using Equibles.Fred.Mcp.Extensions;
using Equibles.Fred.Mcp.Tools;
using Equibles.Mcp;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Equibles.UnitTests.Fred;

public class FredMcpBuilderExtensionsTests {
    [Fact]
    public void AddFred_RegistersAssemblyMcpModuleForFredTools() {
        // AddFred wires the FRED macroeconomic-series MCP tools into the
        // EquiblesMcpBuilder via AssemblyMcpModule<FredTools>. The marker
        // type drives the AutoWiring assembly scan; a regression that
        // swaps it for a non-Fred type would silently miss every FRED
        // MCP tool at runtime. Pin the marker so the regression surfaces
        // here.
        var services = new ServiceCollection();
        var mcpServerBuilder = Substitute.For<IMcpServerBuilder>();
        var builder = new EquiblesMcpBuilder(services, mcpServerBuilder);

        builder.AddFred();

        builder.Modules.Should().ContainSingle()
            .Which.Should().BeOfType<AssemblyMcpModule<FredTools>>();
    }
}

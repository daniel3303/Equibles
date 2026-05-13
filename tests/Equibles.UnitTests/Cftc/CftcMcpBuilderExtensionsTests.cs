using Equibles.Cftc.Mcp.Extensions;
using Equibles.Cftc.Mcp.Tools;
using Equibles.Mcp;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Equibles.UnitTests.Cftc;

public class CftcMcpBuilderExtensionsTests {
    [Fact]
    public void AddCftc_RegistersAssemblyMcpModuleForCftcTools() {
        // AddCftc wires the COT (Commitment of Traders) MCP tools into the
        // EquiblesMcpBuilder via AssemblyMcpModule<CftcTools>. The marker
        // type drives the AutoWiring assembly scan; a regression that
        // swaps it for a non-Cftc type would silently miss every CFTC
        // MCP tool at runtime. Pin the marker so the regression surfaces
        // here.
        var services = new ServiceCollection();
        var mcpServerBuilder = Substitute.For<IMcpServerBuilder>();
        var builder = new EquiblesMcpBuilder(services, mcpServerBuilder);

        builder.AddCftc();

        builder.Modules.Should().ContainSingle()
            .Which.Should().BeOfType<AssemblyMcpModule<CftcTools>>();
    }
}

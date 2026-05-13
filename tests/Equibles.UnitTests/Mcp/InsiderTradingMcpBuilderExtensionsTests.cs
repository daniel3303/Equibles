using Equibles.InsiderTrading.Mcp.Extensions;
using Equibles.InsiderTrading.Mcp.Tools;
using Equibles.Mcp;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Equibles.UnitTests.Mcp;

public class InsiderTradingMcpBuilderExtensionsTests {
    [Fact]
    public void AddInsiderTrading_RegistersAssemblyMcpModuleForInsiderTradingTools() {
        // AddInsiderTrading wires the SEC Form 3/4/5 insider-trading MCP
        // tools into the EquiblesMcpBuilder via
        // AssemblyMcpModule<InsiderTradingTools>. The marker type drives
        // the AutoWiring assembly scan; a regression that swaps it for
        // a non-InsiderTrading type would silently miss every insider
        // MCP tool at runtime. Pin the marker so the regression surfaces
        // here.
        var services = new ServiceCollection();
        var mcpServerBuilder = Substitute.For<IMcpServerBuilder>();
        var builder = new EquiblesMcpBuilder(services, mcpServerBuilder);

        builder.AddInsiderTrading();

        builder.Modules.Should().ContainSingle()
            .Which.Should().BeOfType<AssemblyMcpModule<InsiderTradingTools>>();
    }
}

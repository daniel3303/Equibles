using Equibles.Mcp;
using Equibles.Yahoo.Mcp.Extensions;
using Equibles.Yahoo.Mcp.Tools;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Equibles.UnitTests.Yahoo;

public class YahooMcpBuilderExtensionsTests {
    [Fact]
    public void AddStockPrices_RegistersAssemblyMcpModuleForStockPriceTools() {
        // AddStockPrices wires the Yahoo Finance stock-price MCP tools
        // into the EquiblesMcpBuilder via AssemblyMcpModule<StockPriceTools>.
        // The marker type drives the AutoWiring assembly scan; a regression
        // that swaps it for a non-Yahoo type would silently miss every
        // price-related MCP tool at runtime. Pin the marker so the
        // regression surfaces here.
        var services = new ServiceCollection();
        var mcpServerBuilder = Substitute.For<IMcpServerBuilder>();
        var builder = new EquiblesMcpBuilder(services, mcpServerBuilder);

        builder.AddStockPrices();

        builder.Modules.Should().ContainSingle()
            .Which.Should().BeOfType<AssemblyMcpModule<StockPriceTools>>();
    }
}

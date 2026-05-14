using Equibles.Cboe.Mcp.Extensions;
using Equibles.Cboe.Mcp.Tools;
using Equibles.Mcp;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Equibles.UnitTests.Cboe;

public class CboeMcpBuilderExtensionsTests
{
    [Fact]
    public void AddCboe_RegistersAssemblyMcpModuleForCboeTools()
    {
        // AddCboe wires the CBOE VIX + put/call-ratio MCP tools into the
        // EquiblesMcpBuilder via AssemblyMcpModule<CboeTools>. The marker
        // type drives the AutoWiring assembly scan; a regression that
        // swaps it for a non-Cboe type would silently miss every CBOE
        // MCP tool at runtime. Pin the marker so the regression surfaces
        // here.
        var services = new ServiceCollection();
        var mcpServerBuilder = Substitute.For<IMcpServerBuilder>();
        var builder = new EquiblesMcpBuilder(services, mcpServerBuilder);

        builder.AddCboe();

        builder
            .Modules.Should()
            .ContainSingle()
            .Which.Should()
            .BeOfType<AssemblyMcpModule<CboeTools>>();
    }
}

using Equibles.Congress.Mcp.Extensions;
using Equibles.Congress.Mcp.Tools;
using Equibles.Mcp;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Equibles.UnitTests.Congress;

public class CongressMcpBuilderExtensionsTests
{
    [Fact]
    public void AddCongress_RegistersAssemblyMcpModuleForCongressTools()
    {
        // AddCongress wires the House/Senate disclosure MCP tools into the
        // EquiblesMcpBuilder via AssemblyMcpModule<CongressTools>. The
        // marker type drives the AutoWiring assembly scan; a regression
        // that swaps it for a non-Congress type would silently miss every
        // Congress MCP tool at runtime. Pin the marker so the regression
        // surfaces here.
        var services = new ServiceCollection();
        var mcpServerBuilder = Substitute.For<IMcpServerBuilder>();
        var builder = new EquiblesMcpBuilder(services, mcpServerBuilder);

        builder.AddCongress();

        builder
            .Modules.Should()
            .ContainSingle()
            .Which.Should()
            .BeOfType<AssemblyMcpModule<CongressTools>>();
    }
}

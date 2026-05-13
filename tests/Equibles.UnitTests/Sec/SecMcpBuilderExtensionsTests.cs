using Equibles.Mcp;
using Equibles.Sec.Mcp.Extensions;
using Equibles.Sec.Mcp.Tools;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Equibles.UnitTests.Sec;

public class SecMcpBuilderExtensionsTests {
    [Fact]
    public void AddSec_RegistersAssemblyMcpModuleForRagSearchTools() {
        // AddSec is the composition entry that wires the SEC MCP tools
        // (Ragsearch, FailToDeliver, InsiderTrading, etc.) into the
        // EquiblesMcpBuilder via AssemblyMcpModule<RagSearchTools> — the
        // RagSearchTools type serves as the assembly marker so the
        // AutoWiring scan finds every [McpServerToolType] in the SEC.Mcp
        // assembly. A regression that swaps the marker for a non-SEC type
        // would silently miss every SEC MCP tool at runtime; pin the
        // marker so the regression surfaces here.
        var services = new ServiceCollection();
        var mcpServerBuilder = Substitute.For<IMcpServerBuilder>();
        var builder = new EquiblesMcpBuilder(services, mcpServerBuilder);

        builder.AddSec();

        builder.Modules.Should().ContainSingle()
            .Which.Should().BeOfType<AssemblyMcpModule<RagSearchTools>>();
    }
}

using System.ComponentModel;
using Equibles.Finra.Mcp.Extensions;
using Equibles.Finra.Mcp.Tools;
using Equibles.Mcp;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;
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

    [Fact]
    public void OffExchangeVolumeTools_LivesInTheAssemblyScannedByAddShortData()
    {
        // AddShortData wires AssemblyMcpModule<ShortDataTools>, which calls
        // WithToolsFromAssembly(typeof(ShortDataTools).Assembly) — it scans the
        // WHOLE Equibles.Finra.Mcp assembly, not just ShortDataTools. The
        // off-exchange-volume MCP tool is a separate [McpServerToolType] class
        // in that same assembly, so it is auto-discovered with no extra
        // registration. A regression that moved OffExchangeVolumeTools into a
        // different assembly (e.g. a misplaced refactor) would compile, pass the
        // ShortDataTools registration pin, yet silently drop the off-exchange
        // tool from the MCP server's tool list. Pin that the type ships in the
        // scanned assembly so the regression surfaces here.
        typeof(OffExchangeVolumeTools)
            .Assembly.Should()
            .BeSameAs(typeof(ShortDataTools).Assembly);
    }

    [Fact]
    public void OffExchangeVolumeTools_IsAnMcpServerToolType()
    {
        // WithToolsFromAssembly only discovers classes marked [McpServerToolType];
        // the off-exchange-volume tool will not be exposed without it. A regression
        // that dropped the attribute would compile and pass the assembly-membership
        // pin yet silently leave the tool unregistered. Pin the attribute.
        typeof(OffExchangeVolumeTools)
            .GetCustomAttributes(typeof(McpServerToolTypeAttribute), inherit: false)
            .Should()
            .ContainSingle();
    }

    [Fact]
    public void GetOffExchangeVolume_IsAnMcpServerToolWithADescription()
    {
        // The tool method must carry [McpServerTool] (so it is enumerated) and a
        // [Description] (so the LLM knows what it does). A regression that dropped
        // either attribute would leave the tool invisible or undescribed. Pin both
        // on the GetOffExchangeVolume method.
        var method = typeof(OffExchangeVolumeTools).GetMethod(
            nameof(OffExchangeVolumeTools.GetOffExchangeVolume)
        );

        method.Should().NotBeNull();
        method!
            .GetCustomAttributes(typeof(McpServerToolAttribute), inherit: false)
            .Should()
            .ContainSingle();
        method
            .GetCustomAttributes(typeof(DescriptionAttribute), inherit: false)
            .Should()
            .ContainSingle();
    }
}

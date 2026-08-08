using Equibles.Mcp;
using Equibles.Mcp.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace Equibles.UnitTests.Mcp;

public class EquiblesMcpServiceCollectionExtensionsTests
{
    [Fact]
    public void AddEquiblesMcp_InvokesConfigureCallbackWithNonNullBuilder()
    {
        // AddEquiblesMcp is the composition root's entry point for wiring
        // MCP tooling — it bootstraps the underlying IMcpServerBuilder
        // (with HTTP transport) and exposes an EquiblesMcpBuilder to the
        // caller via a configure callback. The caller chains
        // AddSec/AddCongress/... onto that builder. A regression that
        // skips invoking the callback (or passes a null builder) would
        // silently strip every MCP module registration at startup,
        // leaving the server with no tools to expose. Pin that the
        // callback runs and receives a non-null builder so the
        // regression surfaces here.
        var services = new ServiceCollection();
        EquiblesMcpBuilder captured = null;

        services.AddEquiblesMcp(b => captured = b);

        captured.Should().NotBeNull();
    }

    [Fact]
    public void AddEquiblesMcp_RegistersMemoryCache_ForToolsThatCacheUniverses()
    {
        // Tools that cache an expensive whole-universe computation (the
        // short-squeeze board) resolve IMemoryCache from the container. The
        // builder guarantees the registration so a host that never called
        // AddMemoryCache itself still constructs those tools; without it, tool
        // construction fails at first use with an unresolvable-service error.
        var services = new ServiceCollection();

        services.AddEquiblesMcp(_ => { });

        services
            .Should()
            .Contain(d =>
                d.ServiceType == typeof(Microsoft.Extensions.Caching.Memory.IMemoryCache)
            );
    }
}

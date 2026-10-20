using Equibles.Holdings.BusinessLogic.Extensions;
using Equibles.Holdings.Mcp.Tools;
using Equibles.Mcp;
using Microsoft.Extensions.DependencyInjection;

namespace Equibles.Holdings.Mcp.Extensions;

/// <summary>
/// The holdings tools, scanned from this assembly, with the unified holder list they read
/// registered beside them so every host that adds the module can answer them.
/// </summary>
public class HoldingsMcpModule : IEquiblesMcpModule
{
    public void RegisterTools(IMcpServerBuilder builder, IServiceCollection services)
    {
        services.AddStockOwnership();
        builder.WithToolsFromAssembly(typeof(InstitutionalHoldingsTools).Assembly);
    }
}

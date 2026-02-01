using Equibles.Mcp;
using Microsoft.Extensions.DependencyInjection;

namespace Equibles.Holdings.Mcp;

public class HoldingsMcpModule : IEquiblesMcpModule {
    public void RegisterTools(IMcpServerBuilder builder, IServiceCollection services) {
        builder.WithToolsFromAssembly(typeof(HoldingsMcpModule).Assembly);
    }
}

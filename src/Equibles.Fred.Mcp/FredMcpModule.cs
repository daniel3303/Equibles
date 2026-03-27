using Equibles.Mcp;
using Microsoft.Extensions.DependencyInjection;

namespace Equibles.Fred.Mcp;

public class FredMcpModule : IEquiblesMcpModule {
    public void RegisterTools(IMcpServerBuilder builder, IServiceCollection services) {
        builder.WithToolsFromAssembly(typeof(FredMcpModule).Assembly);
    }
}

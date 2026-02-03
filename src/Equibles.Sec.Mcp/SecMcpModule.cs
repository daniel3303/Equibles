using Equibles.Mcp;
using Microsoft.Extensions.DependencyInjection;

namespace Equibles.Sec.Mcp;

public class SecMcpModule : IEquiblesMcpModule {
    public void RegisterTools(IMcpServerBuilder builder, IServiceCollection services) {
        builder.WithToolsFromAssembly(typeof(SecMcpModule).Assembly);
    }
}

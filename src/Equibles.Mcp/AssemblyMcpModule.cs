using Microsoft.Extensions.DependencyInjection;

namespace Equibles.Mcp;

public class AssemblyMcpModule<TMarker> : IEquiblesMcpModule {
    public void RegisterTools(IMcpServerBuilder builder, IServiceCollection services) {
        builder.WithToolsFromAssembly(typeof(TMarker).Assembly);
    }
}

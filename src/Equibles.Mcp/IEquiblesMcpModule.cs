using Microsoft.Extensions.DependencyInjection;

namespace Equibles.Mcp;

public interface IEquiblesMcpModule {
    void RegisterTools(IMcpServerBuilder builder, IServiceCollection services);
}

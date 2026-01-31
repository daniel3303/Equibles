using Microsoft.Extensions.DependencyInjection;

namespace Equibles.Mcp.Extensions;

public static class ServiceCollectionExtensions {
    public static IServiceCollection AddEquiblesMcp(
        this IServiceCollection services,
        Action<EquiblesMcpBuilder> configureMcp) {
        var mcpServerBuilder = services.AddMcpServer().WithHttpTransport();
        var mcpBuilder = new EquiblesMcpBuilder(services, mcpServerBuilder);
        configureMcp(mcpBuilder);
        return services;
    }
}

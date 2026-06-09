using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace Equibles.Mcp.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddEquiblesMcp(
        this IServiceCollection services,
        Action<EquiblesMcpBuilder> configureMcp
    )
    {
        var mcpServerBuilder = services.AddMcpServer().WithHttpTransport();
        var mcpBuilder = new EquiblesMcpBuilder(services, mcpServerBuilder);
        configureMcp(mcpBuilder);
        WrapToolsWithInvalidParamsTranslation(services);
        return services;
    }

    // The SDK's argument binder throws before any tool body runs, so per-tool
    // try/catch can't reach it — every registered tool is decorated instead (see
    // InvalidParamsTranslatingTool).
    private static void WrapToolsWithInvalidParamsTranslation(IServiceCollection services)
    {
        for (var i = 0; i < services.Count; i++)
        {
            var descriptor = services[i];
            if (descriptor.IsKeyedService || descriptor.ServiceType != typeof(McpServerTool))
            {
                continue;
            }

            var inner = descriptor;
            services[i] = ServiceDescriptor.Describe(
                typeof(McpServerTool),
                provider => new InvalidParamsTranslatingTool(
                    ResolveInner(provider, inner),
                    provider.GetRequiredService<ILogger<InvalidParamsTranslatingTool>>()
                ),
                descriptor.Lifetime
            );
        }
    }

    private static McpServerTool ResolveInner(
        IServiceProvider provider,
        ServiceDescriptor descriptor
    )
    {
        if (descriptor.ImplementationInstance is McpServerTool instance)
        {
            return instance;
        }

        if (descriptor.ImplementationFactory is not null)
        {
            return (McpServerTool)descriptor.ImplementationFactory(provider);
        }

        return (McpServerTool)
            ActivatorUtilities.CreateInstance(provider, descriptor.ImplementationType);
    }
}

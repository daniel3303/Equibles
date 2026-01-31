using Microsoft.Extensions.DependencyInjection;

namespace Equibles.Mcp;

public class EquiblesMcpBuilder {
    internal IServiceCollection Services { get; }
    internal IMcpServerBuilder McpServerBuilder { get; }
    internal List<IEquiblesMcpModule> Modules { get; } = [];
    internal List<Type> MiddlewareTypes { get; } = [];

    public EquiblesMcpBuilder(IServiceCollection services, IMcpServerBuilder mcpServerBuilder) {
        Services = services;
        McpServerBuilder = mcpServerBuilder;
    }

    public EquiblesMcpBuilder AddModule<T>() where T : IEquiblesMcpModule, new() {
        if (Modules.Any(m => m.GetType() == typeof(T))) {
            return this;
        }

        var module = new T();
        Modules.Add(module);
        module.RegisterTools(McpServerBuilder, Services);
        return this;
    }

    public EquiblesMcpBuilder UseMiddleware<T>() where T : class, IEquiblesMcpMiddleware {
        MiddlewareTypes.Add(typeof(T));
        Services.AddScoped<T>();
        return this;
    }
}

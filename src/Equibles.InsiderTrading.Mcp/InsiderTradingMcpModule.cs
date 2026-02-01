using Equibles.Mcp;
using Microsoft.Extensions.DependencyInjection;

namespace Equibles.InsiderTrading.Mcp;

public class InsiderTradingMcpModule : IEquiblesMcpModule {
    public void RegisterTools(IMcpServerBuilder builder, IServiceCollection services) {
        builder.WithToolsFromAssembly(typeof(InsiderTradingMcpModule).Assembly);
    }
}

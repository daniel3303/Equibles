using Equibles.InsiderTrading.Mcp.Tools;
using Equibles.Mcp;

namespace Equibles.InsiderTrading.Mcp.Extensions;

public static class McpBuilderExtensions {
    public static EquiblesMcpBuilder AddInsiderTrading(this EquiblesMcpBuilder builder) {
        return builder.AddModule<AssemblyMcpModule<InsiderTradingTools>>();
    }
}

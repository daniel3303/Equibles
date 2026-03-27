using Equibles.Mcp;

namespace Equibles.Fred.Mcp.Extensions;

public static class McpBuilderExtensions {
    public static EquiblesMcpBuilder AddFred(this EquiblesMcpBuilder builder) {
        return builder.AddModule<FredMcpModule>();
    }
}

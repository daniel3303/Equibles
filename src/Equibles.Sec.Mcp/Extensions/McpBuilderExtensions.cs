using Equibles.Mcp;

namespace Equibles.Sec.Mcp.Extensions;

public static class McpBuilderExtensions {
    public static EquiblesMcpBuilder AddSec(this EquiblesMcpBuilder builder) {
        return builder.AddModule<SecMcpModule>();
    }
}

using Equibles.Mcp;
using Equibles.Sec.Mcp.Tools;

namespace Equibles.Sec.Mcp.Extensions;

public static class McpBuilderExtensions {
    public static EquiblesMcpBuilder AddSec(this EquiblesMcpBuilder builder) {
        return builder.AddModule<AssemblyMcpModule<RagSearchTools>>();
    }
}

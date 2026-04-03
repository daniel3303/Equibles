using Equibles.Finra.Mcp.Tools;
using Equibles.Mcp;

namespace Equibles.Finra.Mcp.Extensions;

public static class McpBuilderExtensions {
    public static EquiblesMcpBuilder AddShortData(this EquiblesMcpBuilder builder) {
        return builder.AddModule<AssemblyMcpModule<ShortDataTools>>();
    }
}

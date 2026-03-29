using Equibles.Cftc.Mcp.Tools;
using Equibles.Mcp;

namespace Equibles.Cftc.Mcp.Extensions;

public static class McpBuilderExtensions {
    public static EquiblesMcpBuilder AddCftc(this EquiblesMcpBuilder builder) {
        return builder.AddModule<AssemblyMcpModule<CftcTools>>();
    }
}

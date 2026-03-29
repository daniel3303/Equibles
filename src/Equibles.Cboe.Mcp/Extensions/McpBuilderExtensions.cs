using Equibles.Cboe.Mcp.Tools;
using Equibles.Mcp;

namespace Equibles.Cboe.Mcp.Extensions;

public static class McpBuilderExtensions {
    public static EquiblesMcpBuilder AddCboe(this EquiblesMcpBuilder builder) {
        return builder.AddModule<AssemblyMcpModule<CboeTools>>();
    }
}

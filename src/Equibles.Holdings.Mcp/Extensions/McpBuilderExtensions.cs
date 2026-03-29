using Equibles.Holdings.Mcp.Tools;
using Equibles.Mcp;

namespace Equibles.Holdings.Mcp.Extensions;

public static class McpBuilderExtensions {
    public static EquiblesMcpBuilder AddHoldings(this EquiblesMcpBuilder builder) {
        return builder.AddModule<AssemblyMcpModule<InstitutionalHoldingsTools>>();
    }
}

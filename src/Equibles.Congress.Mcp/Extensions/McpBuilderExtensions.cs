using Equibles.Congress.Mcp.Tools;
using Equibles.Mcp;

namespace Equibles.Congress.Mcp.Extensions;

public static class McpBuilderExtensions {
    public static EquiblesMcpBuilder AddCongress(this EquiblesMcpBuilder builder) {
        return builder.AddModule<AssemblyMcpModule<CongressTools>>();
    }
}

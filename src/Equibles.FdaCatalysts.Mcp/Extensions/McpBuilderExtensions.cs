using Equibles.FdaCatalysts.Mcp.Tools;
using Equibles.Mcp;

namespace Equibles.FdaCatalysts.Mcp.Extensions;

public static class McpBuilderExtensions
{
    public static EquiblesMcpBuilder AddFdaCatalysts(this EquiblesMcpBuilder builder)
    {
        return builder.AddModule<AssemblyMcpModule<FdaCatalystTools>>();
    }
}

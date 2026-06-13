using Equibles.GovernmentContracts.Mcp.Tools;
using Equibles.Mcp;

namespace Equibles.GovernmentContracts.Mcp.Extensions;

public static class McpBuilderExtensions
{
    public static EquiblesMcpBuilder AddGovernmentContracts(this EquiblesMcpBuilder builder)
    {
        return builder.AddModule<AssemblyMcpModule<GovernmentContractsTools>>();
    }
}

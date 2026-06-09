using Equibles.InvestorRelations.Mcp.Tools;
using Equibles.Mcp;

namespace Equibles.InvestorRelations.Mcp.Extensions;

public static class McpBuilderExtensions
{
    public static EquiblesMcpBuilder AddInvestorRelations(this EquiblesMcpBuilder builder)
    {
        return builder.AddModule<AssemblyMcpModule<InvestorRelationsTools>>();
    }
}

using Equibles.Mcp;
using Equibles.Sec.FinancialFacts.Mcp.Tools;

namespace Equibles.Sec.FinancialFacts.Mcp.Extensions;

public static class McpBuilderExtensions
{
    public static EquiblesMcpBuilder AddFinancialFacts(this EquiblesMcpBuilder builder)
    {
        return builder.AddModule<AssemblyMcpModule<FinancialStatementTools>>();
    }
}

using Equibles.Mcp;
using Equibles.Yahoo.Mcp.Tools;

namespace Equibles.Yahoo.Mcp.Extensions;

public static class McpBuilderExtensions {
    public static EquiblesMcpBuilder AddStockPrices(this EquiblesMcpBuilder builder) {
        return builder.AddModule<AssemblyMcpModule<StockPriceTools>>();
    }
}

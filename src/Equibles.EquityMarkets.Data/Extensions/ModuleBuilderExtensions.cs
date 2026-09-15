using Equibles.CommonStocks.Data.Extensions;
using Equibles.Data;

namespace Equibles.EquityMarkets.Data.Extensions;

public static class ModuleBuilderExtensions
{
    public static EquiblesModuleBuilder AddEquityMarkets(this EquiblesModuleBuilder builder)
    {
        builder.AddCommonStocks();
        return builder.AddModule<EquityMarketsModuleConfiguration>();
    }
}

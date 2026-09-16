using Equibles.Data;
using Equibles.EquityMarkets.Data.Extensions;
using Equibles.Yahoo.Data.Extensions;

namespace Equibles.DelayedTrades.Data.Extensions;

public static class ModuleBuilderExtensions
{
    public static EquiblesModuleBuilder AddDelayedTrades(this EquiblesModuleBuilder builder)
    {
        builder.AddEquityMarkets();
        builder.AddYahoo();
        return builder.AddModule<DelayedTradesModuleConfiguration>();
    }
}

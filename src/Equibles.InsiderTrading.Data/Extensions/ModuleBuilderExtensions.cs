using Equibles.CommonStocks.Data.Extensions;
using Equibles.Data;

namespace Equibles.InsiderTrading.Data.Extensions;
public static class ModuleBuilderExtensions {
    public static EquiblesModuleBuilder AddInsiderTrading(this EquiblesModuleBuilder builder) {
        builder.AddCommonStocks();
        return builder.AddModule<InsiderTradingModuleConfiguration>();
    }
}

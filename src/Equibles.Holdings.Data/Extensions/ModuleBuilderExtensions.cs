using Equibles.CommonStocks.Data.Extensions;
using Equibles.Data;

namespace Equibles.Holdings.Data.Extensions;
public static class ModuleBuilderExtensions {
    public static EquiblesModuleBuilder AddHoldings(this EquiblesModuleBuilder builder) {
        builder.AddCommonStocks();
        return builder.AddModule<HoldingsModuleConfiguration>();
    }
}

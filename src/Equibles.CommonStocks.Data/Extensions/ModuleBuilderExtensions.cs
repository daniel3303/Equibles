using Equibles.Data;

namespace Equibles.CommonStocks.Data.Extensions;
public static class ModuleBuilderExtensions {
    public static EquiblesModuleBuilder AddCommonStocks(this EquiblesModuleBuilder builder) {
        return builder.AddModule<CommonStocksModuleConfiguration>();
    }
}

using Equibles.CommonStocks.Data.Extensions;
using Equibles.Data;

namespace Equibles.Finra.Data.Extensions;
public static class ModuleBuilderExtensions {
    public static EquiblesModuleBuilder AddFinra(this EquiblesModuleBuilder builder) {
        builder.AddCommonStocks();
        return builder.AddModule<FinraModuleConfiguration>();
    }
}

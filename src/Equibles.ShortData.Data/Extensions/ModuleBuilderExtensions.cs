using Equibles.CommonStocks.Data.Extensions;
using Equibles.Data;

namespace Equibles.ShortData.Data.Extensions;
public static class ModuleBuilderExtensions {
    public static EquiblesModuleBuilder AddShortData(this EquiblesModuleBuilder builder) {
        builder.AddCommonStocks();
        return builder.AddModule<ShortDataModuleConfiguration>();
    }
}

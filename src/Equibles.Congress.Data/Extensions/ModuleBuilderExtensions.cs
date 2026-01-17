using Equibles.CommonStocks.Data.Extensions;
using Equibles.Data;

namespace Equibles.Congress.Data.Extensions;
public static class ModuleBuilderExtensions {
    public static EquiblesModuleBuilder AddCongress(this EquiblesModuleBuilder builder) {
        builder.AddCommonStocks();
        return builder.AddModule<CongressModuleConfiguration>();
    }
}

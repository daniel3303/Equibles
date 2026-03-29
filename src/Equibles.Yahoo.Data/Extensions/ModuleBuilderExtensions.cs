using Equibles.CommonStocks.Data.Extensions;
using Equibles.Data;

namespace Equibles.Yahoo.Data.Extensions;

public static class ModuleBuilderExtensions {
    public static EquiblesModuleBuilder AddYahoo(this EquiblesModuleBuilder builder) {
        builder.AddCommonStocks();
        return builder.AddModule<YahooModuleConfiguration>();
    }
}

using Equibles.CommonStocks.Data.Extensions;
using Equibles.Data;
using Equibles.Media.Data.Extensions;

namespace Equibles.Sec.Data.Extensions;
public static class ModuleBuilderExtensions {
    public static EquiblesModuleBuilder AddSec(this EquiblesModuleBuilder builder) {
        builder.AddCommonStocks();
        builder.AddMedia();
        return builder.AddModule<SecModuleConfiguration>();
    }
}

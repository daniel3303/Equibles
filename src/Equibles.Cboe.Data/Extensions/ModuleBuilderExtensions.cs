using Equibles.Data;

namespace Equibles.Cboe.Data.Extensions;

public static class ModuleBuilderExtensions {
    public static EquiblesModuleBuilder AddCboe(this EquiblesModuleBuilder builder) {
        return builder.AddModule<CboeModuleConfiguration>();
    }
}

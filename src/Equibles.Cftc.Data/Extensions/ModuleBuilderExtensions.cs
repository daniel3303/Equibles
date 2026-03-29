using Equibles.Data;

namespace Equibles.Cftc.Data.Extensions;

public static class ModuleBuilderExtensions {
    public static EquiblesModuleBuilder AddCftc(this EquiblesModuleBuilder builder) {
        return builder.AddModule<CftcModuleConfiguration>();
    }
}

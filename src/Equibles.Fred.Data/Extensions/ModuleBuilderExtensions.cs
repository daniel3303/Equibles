using Equibles.Data;

namespace Equibles.Fred.Data.Extensions;

public static class ModuleBuilderExtensions {
    public static EquiblesModuleBuilder AddFred(this EquiblesModuleBuilder builder) {
        return builder.AddModule<FredModuleConfiguration>();
    }
}

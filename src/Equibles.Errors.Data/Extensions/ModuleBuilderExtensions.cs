using Equibles.Data;

namespace Equibles.Errors.Data.Extensions;
public static class ModuleBuilderExtensions {
    public static EquiblesModuleBuilder AddErrors(this EquiblesModuleBuilder builder) {
        return builder.AddModule<ErrorsModuleConfiguration>();
    }
}

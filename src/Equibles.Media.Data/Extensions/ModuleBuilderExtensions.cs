using Equibles.Data;

namespace Equibles.Media.Data.Extensions;
public static class ModuleBuilderExtensions {
    public static EquiblesModuleBuilder AddMedia(this EquiblesModuleBuilder builder) {
        return builder.AddModule<MediaModuleConfiguration>();
    }
}

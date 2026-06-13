using Equibles.Data;

namespace Equibles.FdaCatalysts.Data.Extensions;

public static class ModuleBuilderExtensions
{
    public static EquiblesModuleBuilder AddFdaCatalysts(this EquiblesModuleBuilder builder)
    {
        return builder.AddModule<FdaCatalystsModuleConfiguration>();
    }
}

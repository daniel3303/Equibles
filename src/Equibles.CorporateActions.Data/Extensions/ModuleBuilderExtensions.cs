using Equibles.Data;

namespace Equibles.CorporateActions.Data.Extensions;

public static class ModuleBuilderExtensions
{
    public static EquiblesModuleBuilder AddCorporateActions(this EquiblesModuleBuilder builder)
    {
        return builder.AddModule<CorporateActionsModuleConfiguration>();
    }
}

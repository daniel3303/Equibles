using Equibles.Data;

namespace Equibles.GovernmentContracts.Data.Extensions;

public static class ModuleBuilderExtensions
{
    public static EquiblesModuleBuilder AddGovernmentContracts(this EquiblesModuleBuilder builder)
    {
        return builder.AddModule<GovernmentContractsModuleConfiguration>();
    }
}

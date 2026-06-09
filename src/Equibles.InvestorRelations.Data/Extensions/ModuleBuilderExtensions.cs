using Equibles.CommonStocks.Data.Extensions;
using Equibles.Data;

namespace Equibles.InvestorRelations.Data.Extensions;

public static class ModuleBuilderExtensions
{
    public static EquiblesModuleBuilder AddInvestorRelations(this EquiblesModuleBuilder builder)
    {
        builder.AddCommonStocks();
        return builder.AddModule<InvestorRelationsModuleConfiguration>();
    }
}

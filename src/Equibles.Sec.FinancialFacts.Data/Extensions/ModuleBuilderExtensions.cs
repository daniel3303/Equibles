using Equibles.CommonStocks.Data.Extensions;
using Equibles.Data;
using Equibles.Sec.Data.Extensions;

namespace Equibles.Sec.FinancialFacts.Data.Extensions;

public static class ModuleBuilderExtensions
{
    public static EquiblesModuleBuilder AddFinancialFacts(this EquiblesModuleBuilder builder)
    {
        builder.AddCommonStocks();
        builder.AddSec();
        return builder.AddModule<FinancialFactsModuleConfiguration>();
    }
}

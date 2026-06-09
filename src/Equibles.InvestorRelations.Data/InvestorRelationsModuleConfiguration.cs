using Equibles.InvestorRelations.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.InvestorRelations.Data;

public class InvestorRelationsModuleConfiguration : Equibles.Data.IFinancialModule
{
    public void ConfigureEntities(ModelBuilder builder)
    {
        builder.Entity<IrNewsItem>();
        builder.Entity<IrEvent>();
        builder.Entity<EarningsCalendarEntry>();
    }
}

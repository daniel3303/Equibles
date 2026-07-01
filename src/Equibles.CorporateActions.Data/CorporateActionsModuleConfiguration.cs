using Equibles.CorporateActions.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.CorporateActions.Data;

public class CorporateActionsModuleConfiguration : Equibles.Data.IFinancialModule
{
    public void ConfigureEntities(ModelBuilder builder)
    {
        builder.Entity<StockSplit>().Property(s => s.Source).HasConversion<string>();
        builder.Entity<CashDividend>().Property(d => d.Source).HasConversion<string>();
    }
}

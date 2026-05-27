using Equibles.Cboe.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Cboe.Data;

public class CboeModuleConfiguration : Equibles.Data.IFinancialModule
{
    public void ConfigureEntities(ModelBuilder builder)
    {
        builder.Entity<CboePutCallRatio>();
        builder.Entity<CboeVixDaily>();
    }
}

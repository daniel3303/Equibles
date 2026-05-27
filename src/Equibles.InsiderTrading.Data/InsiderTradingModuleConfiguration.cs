using Equibles.InsiderTrading.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.InsiderTrading.Data;

public class InsiderTradingModuleConfiguration : Equibles.Data.IFinancialModule
{
    public void ConfigureEntities(ModelBuilder builder)
    {
        builder.Entity<InsiderOwner>();
        builder.Entity<InsiderTransaction>();
    }
}

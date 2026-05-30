using Equibles.InsiderTrading.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.InsiderTrading.Data;

public class InsiderTradingModuleConfiguration : Equibles.Data.IFinancialModule
{
    public void ConfigureEntities(ModelBuilder builder)
    {
        builder.Entity<InsiderOwner>();
        builder.Entity<Form144Filing>();
        builder.Entity<Form144PriorSale>();
        // IsPriceValid is intentionally left with no SQL default: a freshly
        // inserted row is null ("not evaluated yet") until the parser (or a
        // maintenance recompute) cross-checks it against the market close.
        builder.Entity<InsiderTransaction>();
    }
}

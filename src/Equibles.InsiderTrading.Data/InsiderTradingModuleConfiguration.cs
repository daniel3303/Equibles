using Equibles.InsiderTrading.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.InsiderTrading.Data;

public class InsiderTradingModuleConfiguration : Equibles.Data.IModuleConfiguration {
    public void ConfigureEntities(ModelBuilder builder) {
        builder.ApplyConfigurationsFromAssembly(typeof(InsiderTradingModuleConfiguration).Assembly);
        builder.Entity<InsiderOwner>();
        builder.Entity<InsiderTransaction>();
    }
}

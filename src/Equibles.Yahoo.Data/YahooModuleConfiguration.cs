using Equibles.Data;
using Equibles.Yahoo.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Yahoo.Data;

public class YahooModuleConfiguration : IModuleConfiguration {
    public void ConfigureEntities(ModelBuilder builder) {
        builder.Entity<DailyStockPrice>();
    }
}

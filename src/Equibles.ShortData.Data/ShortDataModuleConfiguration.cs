using Equibles.ShortData.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.ShortData.Data;

public class ShortDataModuleConfiguration : Equibles.Data.IModuleConfiguration {
    public void ConfigureEntities(ModelBuilder builder) {
        builder.ApplyConfigurationsFromAssembly(typeof(ShortDataModuleConfiguration).Assembly);
        builder.Entity<DailyShortVolume>();
        builder.Entity<FailToDeliver>();
        builder.Entity<ShortInterest>();
    }
}

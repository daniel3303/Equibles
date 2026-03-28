using Equibles.Finra.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Finra.Data;

public class FinraModuleConfiguration : Equibles.Data.IModuleConfiguration {
    public void ConfigureEntities(ModelBuilder builder) {
        builder.Entity<DailyShortVolume>();
        builder.Entity<ShortInterest>();
    }
}

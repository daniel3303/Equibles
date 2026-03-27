using Equibles.Fred.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Fred.Data;

public class FredModuleConfiguration : Equibles.Data.IModuleConfiguration {
    public void ConfigureEntities(ModelBuilder builder) {
        builder.Entity<FredSeries>();
        builder.Entity<FredObservation>();
    }
}

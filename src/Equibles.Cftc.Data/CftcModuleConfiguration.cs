using Equibles.Cftc.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Cftc.Data;

public class CftcModuleConfiguration : Equibles.Data.IModuleConfiguration {
    public void ConfigureEntities(ModelBuilder builder) {
        builder.Entity<CftcContract>();
        builder.Entity<CftcPositionReport>();
    }
}

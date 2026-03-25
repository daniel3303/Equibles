using Equibles.Congress.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Congress.Data;

public class CongressModuleConfiguration : Equibles.Data.IModuleConfiguration {
    public void ConfigureEntities(ModelBuilder builder) {
        builder.Entity<CongressMember>();
        builder.Entity<CongressionalTrade>();
    }
}

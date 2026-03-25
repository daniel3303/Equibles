using Equibles.Media.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Media.Data;

public class MediaModuleConfiguration : Equibles.Data.IModuleConfiguration {
    public void ConfigureEntities(ModelBuilder builder) {
        builder.Entity<Models.File>();
        builder.Entity<FileContent>();
        builder.Entity<Image>();
    }
}

using Equibles.Errors.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Errors.Data;

public class ErrorsModuleConfiguration : Equibles.Data.IModuleConfiguration {
    public void ConfigureEntities(ModelBuilder builder) {
        // ErrorSource smart enum stored as string
        builder.Entity<Error>(b => {
            b.Property(e => e.Source)
                .HasConversion(
                    v => v.Value,
                    v => new ErrorSource(v));
        });
    }
}

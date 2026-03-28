using Equibles.Sec.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Sec.Data;

public class SecModuleConfiguration : Equibles.Data.IModuleConfiguration {
    public void ConfigureEntities(ModelBuilder builder) {
        var docTypeConversion = new Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DocumentType, string>(
            v => v.Value,
            v => DocumentType.FromValue(v) ?? new DocumentType(v));

        builder.Entity<Document>(b => {
            b.Property(e => e.DocumentType).HasConversion(docTypeConversion);
        });

        builder.Entity<Models.Chunks.Chunk>(b => {
            b.Property(e => e.DocumentType).HasConversion(docTypeConversion);
        });

        builder.Entity<FailToDeliver>();
    }
}

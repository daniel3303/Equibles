using Equibles.Media.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Equibles.Media.Data;

public class MediaModuleConfiguration : Equibles.Data.IFinancialModule
{
    public void ConfigureEntities(ModelBuilder builder)
    {
        // Store the StorageProvider smart enum as a string; the fallback tolerates
        // unknown/NULL column values (following the DocumentType convention). The DB
        // default backfills existing rows to "Database" during the migration.
        var storageProviderConversion = new ValueConverter<StorageProvider, string>(
            v => v.Value,
            v => StorageProvider.FromValue(v) ?? new StorageProvider(v)
        );

        builder.Entity<Models.File>(b =>
        {
            b.Property(e => e.StorageProvider)
                .HasConversion(storageProviderConversion)
                .HasMaxLength(16)
                .HasDefaultValue(StorageProvider.Database);

            // Lets the migration drain worker cheaply find rows still stored in the database.
            b.HasIndex(e => e.StorageProvider);
        });
        builder.Entity<FileContent>();
        builder.Entity<Image>();
    }
}

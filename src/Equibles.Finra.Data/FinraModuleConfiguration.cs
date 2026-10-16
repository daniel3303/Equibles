using Equibles.Finra.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Finra.Data;

public class FinraModuleConfiguration : Equibles.Data.IFinancialModule
{
    public void ConfigureEntities(ModelBuilder builder)
    {
        builder
            .Entity<DailyShortVolume>()
            .HasOne(row => row.Listing)
            .WithMany()
            .HasForeignKey(row => row.EquityListingId)
            .OnDelete(DeleteBehavior.Restrict);
        // A listing's daily rows sit one per heap page, so reading a few years of history touched
        // about a thousand pages. Carrying the volume figures on the (listing, date) key lets that
        // read resolve as an index-only scan. The explicit name keeps it distinct from the key-only
        // index it replaces, so the migration builds it concurrently before dropping that one.
        builder
            .Entity<DailyShortVolume>()
            .HasIndex(row => new { row.EquityListingId, row.Date })
            .IsUnique()
            .HasDatabaseName("IX_DailyShortVolume_EquityListingId_Date_Covering")
            .IncludeProperties(row => new
            {
                row.ShortVolume,
                row.ShortExemptVolume,
                row.TotalVolume,
            });
        builder.Entity<FinraImportPartition>();
        builder
            .Entity<OffExchangeVolume>()
            .HasOne(row => row.Listing)
            .WithMany()
            .HasForeignKey(row => row.EquityListingId)
            .OnDelete(DeleteBehavior.Restrict);
        builder
            .Entity<ShortInterest>()
            .HasOne(row => row.Listing)
            .WithMany()
            .HasForeignKey(row => row.EquityListingId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

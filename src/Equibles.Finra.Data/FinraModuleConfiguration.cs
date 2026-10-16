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
        // Carrying the volume figures on the (listing, date) key lets a listing's history read as an
        // index-only scan; the explicit name lets the migration build it before dropping the key-only one.
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

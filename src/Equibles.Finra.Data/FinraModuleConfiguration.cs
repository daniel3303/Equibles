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

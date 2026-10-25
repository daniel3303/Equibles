using Equibles.Data;
using Equibles.Yahoo.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Yahoo.Data;

public class YahooModuleConfiguration : IFinancialModule
{
    public void ConfigureEntities(ModelBuilder builder)
    {
        builder
            .Entity<EquityDailyStockPrice>()
            .HasOne(price => price.Listing)
            .WithMany()
            .HasForeignKey(price => price.EquityListingId)
            .OnDelete(DeleteBehavior.Restrict);
        // One bar per listing and date. The INCLUDE list answers the 13F valuation's 7-day close
        // windows (Close, Volume, SourceTicker) from the index alone; bars are stored in date
        // order, so a heap read would cost one page per bar.
        builder
            .Entity<EquityDailyStockPrice>()
            .HasIndex(price => new { price.EquityListingId, price.Date })
            .IsUnique()
            .HasDatabaseName("IX_EquityDailyStockPrice_ListingSeries")
            .IncludeProperties(price => new
            {
                price.Close,
                price.Volume,
                price.SourceTicker,
            });
        builder
            .Entity<UnattributedDailyStockPrice>()
            .HasOne(price => price.Issuer)
            .WithMany()
            .HasForeignKey(price => price.EquityIssuerId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

using Equibles.DelayedTrades.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.DelayedTrades.Data;

public class DelayedTradesModuleConfiguration : Equibles.Data.IFinancialModule
{
    public void ConfigureEntities(ModelBuilder builder)
    {
        builder
            .Entity<LatestDelayedTrade>()
            .HasOne(row => row.Listing)
            .WithMany()
            .HasForeignKey(row => row.EquityListingId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.Entity<DelayedTradeImportPartition>();
        builder.Entity<DelayedTradeFileCapture>();
    }
}

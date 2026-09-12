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
        builder
            .Entity<UnattributedDailyStockPrice>()
            .HasOne(price => price.Issuer)
            .WithMany()
            .HasForeignKey(price => price.EquityIssuerId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

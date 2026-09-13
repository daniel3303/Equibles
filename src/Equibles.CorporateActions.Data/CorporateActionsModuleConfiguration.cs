using Equibles.CorporateActions.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.CorporateActions.Data;

public class CorporateActionsModuleConfiguration : Equibles.Data.IFinancialModule
{
    public void ConfigureEntities(ModelBuilder builder)
    {
        builder
            .Entity<StockSplit>()
            .HasOne(row => row.Issuer)
            .WithMany()
            .HasForeignKey(row => row.EquityIssuerId)
            .OnDelete(DeleteBehavior.Restrict);
        builder
            .Entity<CashDividend>()
            .HasOne(row => row.Issuer)
            .WithMany()
            .HasForeignKey(row => row.EquityIssuerId)
            .OnDelete(DeleteBehavior.Restrict);
        var stockSplit = builder.Entity<StockSplit>();
        stockSplit
            .HasOne(row => row.Listing)
            .WithMany()
            .HasForeignKey(row => row.EquityListingId)
            .OnDelete(DeleteBehavior.Restrict);
        builder
            .Entity<CashDividend>()
            .HasOne(row => row.Listing)
            .WithMany()
            .HasForeignKey(row => row.EquityListingId)
            .OnDelete(DeleteBehavior.Restrict);
        stockSplit
            .HasIndex(row => new { row.EquityListingId, row.EffectiveDate })
            .IsUnique()
            .HasFilter("\"EquityListingId\" IS NOT NULL");
        builder
            .Entity<CashDividend>()
            .HasIndex(row => new { row.EquityListingId, row.ExDate })
            .IsUnique()
            .HasFilter("\"EquityListingId\" IS NOT NULL");
        builder
            .Entity<CashDividend>()
            .HasIndex(row => new { row.EquityIssuerId, row.ExDate })
            .IsUnique()
            .HasFilter("\"EquityListingId\" IS NULL");
        stockSplit.Property(s => s.Source).HasConversion<string>();
        stockSplit
            .HasIndex(s => new
            {
                s.EquityIssuerId,
                s.PriceSeriesTicker,
                s.EffectiveDate,
            })
            .IsUnique()
            .HasFilter("\"PriceSeriesTicker\" IS NOT NULL AND \"EquityListingId\" IS NULL");
        stockSplit
            .HasIndex(s => new { s.EquityIssuerId, s.EffectiveDate })
            .IsUnique()
            .HasFilter("\"PriceSeriesTicker\" IS NULL AND \"EquityListingId\" IS NULL");
        builder.Entity<CashDividend>().Property(d => d.Source).HasConversion<string>();
        builder
            .Entity<CorporateActionPriceReconciliationCursor>()
            .HasData(
                new CorporateActionPriceReconciliationCursor
                {
                    Name = CorporateActionPriceReconciliationCursor.DefaultName,
                }
            );
    }
}

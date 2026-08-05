using Equibles.Data;
using Equibles.Yahoo.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Yahoo.Data;

public class YahooModuleConfiguration : IFinancialModule
{
    public void ConfigureEntities(ModelBuilder builder)
    {
        builder.Entity<LegacyDailyStockPrice>(prices =>
        {
            prices.ToTable("DailyStockPrice");
            prices.HasKey(p => p.Id).HasName("PK_DailyStockPrice");
            prices
                .HasOne(p => p.CommonStock)
                .WithMany()
                .HasForeignKey(p => p.CommonStockId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_DailyStockPrice_CommonStock_CommonStockId");
            prices.HasIndex(p => p.Date).HasDatabaseName("IX_DailyStockPrice_Date");
            prices
                .HasIndex(p => new { p.CommonStockId, p.Date })
                .HasDatabaseName("IX_DailyStockPrice_CommonStockId_Date")
                .IsUnique();
        });

        builder.Entity<DailyStockPrice>(prices =>
        {
            // Keep current price repositories on isolated exact-listing storage. The schema-only
            // LegacyDailyStockPrice mapping above retains the historical table in the model, so
            // retiring binaries can keep using it without ever seeing exact secondary series.
            prices.ToTable("ListedDailyStockPrice");
            prices.HasKey(p => p.Id).HasName("PK_ListedDailyStockPrice");
            prices
                .HasOne(p => p.CommonStock)
                .WithMany()
                .HasForeignKey(p => p.CommonStockId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_ListedDailyStockPrice_CommonStock_CommonStockId");

            prices.HasIndex(p => p.Date).HasDatabaseName("IX_ListedDailyStockPrice_Date");

            prices
                .HasIndex(p => new
                {
                    p.CommonStockId,
                    p.ListedTicker,
                    p.Date,
                })
                .HasDatabaseName("IX_ListedDailyStockPrice_CommonStockId_ListedTicker_Date")
                .IsUnique();
        });
    }
}

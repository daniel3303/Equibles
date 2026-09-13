using Equibles.CommonStocks.Data.Models;
using Equibles.Data;
using Equibles.Yahoo.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.TestSupport;

// Historical migration fixtures only; current application contexts never register these stores.
internal sealed class LegacyEquityTestMappings : IModuleConfiguration
{
    // AddAllModules scans loaded test assemblies; only historical fixtures may opt into this model.
    internal LegacyEquityTestMappings() { }

    public void ConfigureEntities(ModelBuilder builder)
    {
        var commonStock = builder.Entity<CommonStock>();
        commonStock.Property(stock => stock.Active).HasDefaultValue(true);
        commonStock
            .Property(stock => stock.ReferenceTickers)
            .IsRequired()
            .HasDefaultValueSql("'{}'::text[]");
        commonStock.HasIndex(stock => stock.Ticker).IsUnique().HasFilter("\"Active\"");
        builder.Entity<EquityIssuer>().Property<Guid?>("CommonStockId");
        builder.Entity<EquityIssuer>().HasIndex("CommonStockId").IsUnique();
        builder
            .Entity<EquityIssuer>()
            .HasOne<CommonStock>()
            .WithOne()
            .HasForeignKey<EquityIssuer>("CommonStockId")
            .OnDelete(DeleteBehavior.SetNull);
        builder
            .Entity<LegacyEquityListing>()
            .HasOne(row => row.Listing)
            .WithOne()
            .HasForeignKey<LegacyEquityListing>(row => row.EquityListingId)
            .OnDelete(DeleteBehavior.Restrict);
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

    public static IQueryable<EquityListing> GetListing(
        DbContext context,
        Guid issuerId,
        string ticker
    ) =>
        context
            .Set<LegacyEquityListing>()
            .Where(row => row.CommonStockId == issuerId && row.ListedTicker == ticker)
            .Select(row => row.Listing);
}

using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Data.Models.Taxonomies;
using Microsoft.EntityFrameworkCore;

namespace Equibles.CommonStocks.Data;

public class CommonStocksModuleConfiguration : Equibles.Data.IFinancialModule
{
    public void ConfigureEntities(ModelBuilder builder)
    {
        var commonStock = builder.Entity<CommonStock>();
        commonStock.Property(stock => stock.Active).HasDefaultValue(true);
        commonStock
            .Property(stock => stock.ReferenceTickers)
            .IsRequired()
            .HasDefaultValueSql("'{}'::text[]");
        commonStock.HasIndex(stock => stock.Ticker).IsUnique().HasFilter("\"Active\"");
        builder.Entity<CommonStockCusipAlias>();
        builder.Entity<CommonStockDelistedListing>();
        builder.Entity<CommonStockListedCusip>();
        builder.Entity<CommonStockTickerAlias>();
        builder.Entity<CommonStockTickerEvidence>();
        ConfigureEquityIdentity(builder);
        builder.Entity<Industry>();
        builder.Entity<Sector>();
    }

    private static void ConfigureEquityIdentity(ModelBuilder builder)
    {
        builder
            .Entity<EquityIssuer>()
            .HasOne(row => row.CommonStock)
            .WithOne()
            .HasForeignKey<EquityIssuer>(row => row.CommonStockId)
            .OnDelete(DeleteBehavior.SetNull);
        builder
            .Entity<EquitySecurity>()
            .HasOne(row => row.Issuer)
            .WithMany()
            .HasForeignKey(row => row.EquityIssuerId)
            .OnDelete(DeleteBehavior.Restrict);
        builder
            .Entity<LegacyEquityListing>()
            .HasOne(row => row.Listing)
            .WithOne()
            .HasForeignKey<LegacyEquityListing>(row => row.EquityListingId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.Entity<EquityListing>(listing =>
        {
            listing
                .HasOne(row => row.Security)
                .WithMany()
                .HasForeignKey(row => row.EquitySecurityId)
                .OnDelete(DeleteBehavior.Restrict);
            listing
                .HasIndex(row => new { row.MarketIdentifierCode, row.Ticker })
                .IsUnique()
                .HasFilter("\"Active\"");
            listing.ToTable(
                "EquityListing",
                table =>
                {
                    table.HasCheckConstraint(
                        "CK_EquityListing_Verified",
                        "\"IdentityState\" IN (0, 1) AND (\"IdentityState\" = 0 OR (\"MarketIdentifierCode\" IS NOT NULL AND \"TradingCurrency\" IS NOT NULL AND \"QuoteUnitMultiplier\" IS NOT NULL AND nullif(btrim(\"IdentitySourceUrl\"), '') IS NOT NULL))"
                    );
                    table.HasCheckConstraint(
                        "CK_EquityListing_Mic",
                        "\"MarketIdentifierCode\" ~ '^[A-Z0-9]{4}$'"
                    );
                    table.HasCheckConstraint(
                        "CK_EquityListing_Currency",
                        "\"TradingCurrency\" ~ '^[A-Z]{3}$'"
                    );
                    table.HasCheckConstraint(
                        "CK_EquityListing_Ticker",
                        "\"IdentityState\" = 0 OR (length(btrim(\"Ticker\")) > 0 AND \"Ticker\" = btrim(\"Ticker\"))"
                    );
                    table.HasCheckConstraint(
                        "CK_EquityListing_QuoteUnitMultiplier",
                        "\"QuoteUnitMultiplier\" > 0"
                    );
                    table.HasCheckConstraint(
                        "CK_EquityListing_Lifecycle",
                        "(NOT \"Active\" OR \"DelistedOn\" IS NULL) AND (\"ListedOn\" IS NULL OR \"DelistedOn\" IS NULL OR \"ListedOn\" <= \"DelistedOn\")"
                    );
                }
            );
        });
    }
}

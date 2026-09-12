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
        builder
            .Entity<EquityIssuerCusipAlias>()
            .HasOne(row => row.Issuer)
            .WithMany()
            .HasForeignKey(row => row.EquityIssuerId)
            .OnDelete(DeleteBehavior.Restrict);
        builder
            .Entity<EquityListingRetirementEvidence>()
            .HasOne(row => row.Issuer)
            .WithMany()
            .HasForeignKey(row => row.EquityIssuerId)
            .OnDelete(DeleteBehavior.Restrict);
        builder
            .Entity<EquityListingCusipEvidence>()
            .HasOne(row => row.Issuer)
            .WithMany()
            .HasForeignKey(row => row.EquityIssuerId)
            .OnDelete(DeleteBehavior.Restrict);
        builder
            .Entity<EquityIssuerTickerAlias>()
            .HasOne(row => row.Issuer)
            .WithMany()
            .HasForeignKey(row => row.EquityIssuerId)
            .OnDelete(DeleteBehavior.Restrict);
        builder
            .Entity<EquityIssuerTickerEvidence>()
            .HasOne(row => row.Issuer)
            .WithMany()
            .HasForeignKey(row => row.EquityIssuerId)
            .OnDelete(DeleteBehavior.Restrict);
        ConfigureEquityIdentity(builder);
        builder.Entity<Industry>();
        builder.Entity<Sector>();
    }

    private static void ConfigureEquityIdentity(ModelBuilder builder)
    {
        builder.Entity<EquityDirectorySourceRecord>();
        builder.Entity<EquityIssuer>().Property(issuer => issuer.Id).ValueGeneratedNever();
        builder.Entity<EquitySecurity>().Property(security => security.Id).ValueGeneratedNever();
        builder.Entity<EquityListing>().Property(listing => listing.Id).ValueGeneratedNever();
        builder
            .Entity<EquityIssuer>()
            .HasOne(row => row.CommonStock)
            .WithOne()
            .HasForeignKey<EquityIssuer>(row => row.CommonStockId)
            .OnDelete(DeleteBehavior.SetNull);
        builder
            .Entity<EquitySecurity>()
            .HasOne(row => row.Issuer)
            .WithMany(row => row.Securities)
            .HasForeignKey(row => row.EquityIssuerId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.Entity<EquityIssuer>().HasIndex(row => row.Cik).IsUnique();
        builder.Entity<EquityIssuerPresentation>(presentation =>
        {
            presentation
                .HasOne(row => row.Issuer)
                .WithOne(row => row.Presentation)
                .HasForeignKey<EquityIssuerPresentation>(row => row.EquityIssuerId)
                .OnDelete(DeleteBehavior.Cascade);
            presentation
                .HasOne(row => row.Listing)
                .WithMany()
                .HasForeignKey(row => row.EquityListingId)
                .OnDelete(DeleteBehavior.Restrict);
        });
        builder
            .Entity<LegacyEquityListing>()
            .HasOne(row => row.Listing)
            .WithOne()
            .HasForeignKey<LegacyEquityListing>(row => row.EquityListingId)
            .OnDelete(DeleteBehavior.Restrict);
        builder
            .Entity<EquityListingTickerAlias>()
            .HasOne(alias => alias.Listing)
            .WithMany(listing => listing.TickerAliases)
            .HasForeignKey(alias => alias.EquityListingId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.Entity<EquityListing>(listing =>
        {
            listing.HasIndex(row => new { row.MarketCountryCode, row.Ticker });
            listing
                .HasOne(row => row.Security)
                .WithMany(row => row.Listings)
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
                        "CK_EquityListing_MarketCountryCode",
                        "\"MarketCountryCode\" ~ '^[A-Z]{2}$'"
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

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
        commonStock.Property(stock => stock.ReferenceTickers)
            .IsRequired()
            .HasDefaultValueSql("'{}'::text[]");
        commonStock.HasIndex(stock => stock.Ticker).IsUnique().HasFilter("\"Active\"");
        builder.Entity<CommonStockCusipAlias>();
        builder.Entity<CommonStockDelistedListing>();
        builder.Entity<CommonStockListedCusip>();
        builder.Entity<CommonStockTickerAlias>();
        builder.Entity<CommonStockTickerEvidence>();
        builder.Entity<Industry>();
        builder.Entity<Sector>();
    }
}

using Equibles.CorporateActions.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.CorporateActions.Data;

public class CorporateActionsModuleConfiguration : Equibles.Data.IFinancialModule
{
    public void ConfigureEntities(ModelBuilder builder)
    {
        var stockSplit = builder.Entity<StockSplit>();
        stockSplit.Property(s => s.Source).HasConversion<string>();
        stockSplit
            .HasIndex(s => new
            {
                s.CommonStockId,
                s.PriceSeriesTicker,
                s.EffectiveDate,
            })
            .IsUnique()
            .HasFilter("\"PriceSeriesTicker\" IS NOT NULL");
        stockSplit
            .HasIndex(s => new { s.CommonStockId, s.EffectiveDate })
            .IsUnique()
            .HasFilter("\"PriceSeriesTicker\" IS NULL");
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

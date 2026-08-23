using Equibles.Congress.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Congress.Data;

public class CongressModuleConfiguration : Equibles.Data.IFinancialModule
{
    public void ConfigureEntities(ModelBuilder builder)
    {
        builder.Entity<CongressMember>();
        // These values are written by the scraper. The defaults made the additive migration safe
        // for existing rows; ValueGeneratedNever makes them usable in FlexLabs' conflict key.
        ConfigureRequiredTradeText(builder, t => t.OwnerType);
        ConfigureRequiredTradeText(builder, t => t.AssetType);
        ConfigureRequiredTradeText(builder, t => t.Subholding);
        ConfigureRequiredTradeText(builder, t => t.FiledTicker);
        builder
            .Entity<CongressionalTrade>()
            .HasOne(trade => trade.CommonStock)
            .WithMany()
            .HasForeignKey(trade => trade.CommonStockId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.Entity<CongressionalAnnualDisclosure>();
        builder.Entity<CongressionalDisclosureLine>();
        builder.Entity<CongressionalFilingRecord>();
        builder.Entity<CongressionalTradeImportPartition>();
        builder.Entity<CongressMemberRedirect>();
    }

    private static void ConfigureRequiredTradeText(
        ModelBuilder builder,
        System.Linq.Expressions.Expression<Func<CongressionalTrade, string>> property
    ) =>
        builder
            .Entity<CongressionalTrade>()
            .Property(property)
            .HasDefaultValue("")
            .ValueGeneratedNever();
}

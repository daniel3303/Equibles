using Equibles.InsiderTrading.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.InsiderTrading.Data;

public class InsiderTradingModuleConfiguration : Equibles.Data.IFinancialModule
{
    public void ConfigureEntities(ModelBuilder builder)
    {
        builder.Entity<InsiderOwner>();
        builder.Entity<Form144Filing>();
        builder.Entity<Form144PriorSale>();
        builder.Entity<InsiderFiling>();
        // IsPriceValid is intentionally left with no SQL default: a freshly
        // inserted row is null ("not evaluated yet") until the parser (or a
        // maintenance recompute) cross-checks it against the market close.
        builder.Entity<InsiderTransaction>();
        // Notes is a NOT NULL text[]; default existing rows to an empty array so
        // the column can be added without a backfill (the reprocess pass fills it).
        // IsRequired is explicit because nullable reference types are off, so EF
        // would otherwise treat the collection as optional.
        builder
            .Entity<InsiderTransaction>()
            .Property(t => t.Notes)
            .IsRequired()
            .HasDefaultValueSql("'{}'");
    }
}

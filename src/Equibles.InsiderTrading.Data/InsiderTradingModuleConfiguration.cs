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

        // Covering index for the insider-trading dashboard's "top by dollar
        // volume" queries (run three times per page: buys, sells, biggest).
        // Each filters a ~90-day TransactionDate window, drops invalid-price and
        // derivative rows, then orders by Shares * PricePerShare. The date window
        // is the only selective filter, but the planner was choosing a full seq
        // scan over the plain [Index(TransactionDate)] btree; the INCLUDE columns
        // let the window resolve as an index-only scan (no heap fetch for the
        // filter/sort fields), turning an ~805ms scan into ~90ms. Postgres-specific
        // INCLUDE isn't expressible via the [Index] attribute, so it lives here;
        // EF merges it with the entity's [Index(TransactionDate)] attribute into a
        // single btree with the INCLUDE list attached.
        builder
            .Entity<InsiderTransaction>()
            .HasIndex(t => t.TransactionDate)
            .IncludeProperties(t => new
            {
                t.Shares,
                t.PricePerShare,
                t.IsPriceValid,
                t.SecurityKind,
                t.SecurityTitle,
            });
    }
}

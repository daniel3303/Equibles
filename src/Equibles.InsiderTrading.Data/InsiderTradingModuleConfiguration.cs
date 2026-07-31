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

        // Covering index for every ~90-day TransactionDate window scan. Two
        // consumers share it:
        //   - the insider-trading dashboard's "top by dollar volume" boards (run
        //     three times per page: buys, sells, biggest), which drop invalid-price
        //     and derivative rows then order by Shares * PricePerShare;
        //   - the insider-sentiment scoring pass, which additionally groups by
        //     CommonStockId, counts distinct InsiderOwnerId per direction, and
        //     gates on TransactionCode / IsRule10b5One.
        // The date window is the only selective filter, but the planner was
        // choosing a full seq scan over the plain [Index(TransactionDate)] btree;
        // the INCLUDE columns let the window resolve as an index-only scan (no heap
        // fetch for the filter/sort/group fields). The sentiment gate's four
        // columns were missing from the original five, so that query fell back to
        // heap-fetching every candidate row: 124k fetches over a 90-day window,
        // 66,497 buffers, ~84ms warm but 17-30s cold — which timed out the 30s
        // CommandTimeout on a cold cache. With them included it is an index-only
        // scan: 2,511 buffers, zero heap fetches.
        // Postgres-specific INCLUDE isn't expressible via the [Index] attribute, so
        // it lives here; EF merges it with the entity's [Index(TransactionDate)]
        // attribute into a single btree with the INCLUDE list attached. The
        // explicit name keeps the widened index distinct from the five-column one
        // it supersedes, so the migration can build the replacement CONCURRENTLY
        // before dropping the old one instead of rebuilding in place under lock.
        builder
            .Entity<InsiderTransaction>()
            .HasIndex(t => t.TransactionDate)
            .HasDatabaseName("IX_InsiderTransaction_TransactionDate_Covering")
            .IncludeProperties(t => new
            {
                t.Shares,
                t.PricePerShare,
                t.IsPriceValid,
                t.SecurityKind,
                t.SecurityTitle,
                t.CommonStockId,
                t.InsiderOwnerId,
                t.TransactionCode,
                t.IsRule10b5One,
            });
    }
}

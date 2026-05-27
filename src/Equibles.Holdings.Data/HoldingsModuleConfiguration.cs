using Equibles.Holdings.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Holdings.Data;

public class HoldingsModuleConfiguration : Equibles.Data.IFinancialModule
{
    public void ConfigureEntities(ModelBuilder builder)
    {
        // Holdings unique index: include OptionType and FilingType with NULLS NOT DISTINCT (cannot be expressed via attributes)
        builder
            .Entity<InstitutionalHolding>()
            .HasIndex(h => new
            {
                h.CommonStockId,
                h.InstitutionalHolderId,
                h.ReportDate,
                h.ShareType,
                h.OptionType,
                h.FilingType,
            })
            .IsUnique()
            .AreNullsDistinct(false);

        // Covering index for the per-stock ownership-trend GROUP BY on the stock
        // Holdings page. Postgres-specific `INCLUDE` is not expressible via the
        // [Index] attribute, so it lives here. Holders / value / shares ride along
        // with the indexed (CommonStockId, ReportDate) tuple so the trend rollup
        // runs as an index-only scan instead of a bitmap heap scan with lossy
        // blocks — a heavily-held name like AAPL has ~76k holdings across 18+
        // quarters and the heap fetch dominated cold load time. EF merges this
        // with the entity's `[Index(CommonStockId, ReportDate)]` attribute, so
        // there's a single btree on those columns with the INCLUDE list attached.
        builder
            .Entity<InstitutionalHolding>()
            .HasIndex(h => new { h.CommonStockId, h.ReportDate })
            .IncludeProperties(h => new
            {
                h.InstitutionalHolderId,
                h.Value,
                h.Shares,
            });

        builder.Entity<ProcessedDataSet>();
        builder.Entity<ProcessedFiling>();
        builder.Entity<RealtimeSweepState>();

        // AumQuarterlySnapshot uses ReportDate as the primary key. The [Key]
        // attribute can't be paired with [DatabaseGenerated(None)] without EF
        // also treating it as identity-by-convention on integral types, but the
        // intent is identical here — DateOnly key, caller-supplied. Configured
        // via Fluent API to keep the entity declaration attribute-only.
        builder.Entity<AumQuarterlySnapshot>().HasKey(s => s.ReportDate);

        // SectorQuarterlySnapshot uses a composite (ReportDate, SectorId) key,
        // which the [Key] attribute cannot express. Reads on /holdings/trends
        // scan the whole table ordered by ReportDate, then SectorName — the
        // composite key already covers the ordering by date, so no further
        // index is needed.
        builder
            .Entity<SectorQuarterlySnapshot>()
            .HasKey(s => new { s.ReportDate, s.SectorId });
    }
}

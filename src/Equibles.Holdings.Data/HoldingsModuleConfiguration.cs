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

        // Covering index for the per-holder portfolio rollups on the holder page
        // (StocksController.ShowHolder → ComputeTopPortfolioPositions /
        // HolderPortfolioProvider.GetTrend). Both filter by holder and GROUP BY
        // either ReportDate or CommonStockId while summing Value / Shares; without
        // the INCLUDE list those run as a bitmap heap scan, and under crawler
        // concurrency the slow reads drained the portal's Financial pool (#1262).
        // Mirrors the per-stock covering index above so the rollups are
        // index-only scans. EF merges this with the entity's
        // `[Index(InstitutionalHolderId, ReportDate)]` attribute into one btree.
        builder
            .Entity<InstitutionalHolding>()
            .HasIndex(h => new { h.InstitutionalHolderId, h.ReportDate })
            .IncludeProperties(h => new
            {
                h.CommonStockId,
                h.Value,
                h.Shares,
            });

        // Covering index for the per-stock 13F ranking pages (Most-Held Stocks,
        // Last-Quarter Movers): WHERE ReportDate = <quarter> GROUP BY CommonStockId
        // with COUNT(DISTINCT InstitutionalHolderId) and SUM(Shares)/SUM(Value).
        // Leading with ReportDate lets the quarter filter seek; rows then arrive
        // ordered by (CommonStockId, InstitutionalHolderId) so the grouped
        // aggregate + distinct count run as an index-only scan with no sort. The
        // per-stock covering index above leads with CommonStockId and so can't
        // serve this ReportDate-first ranking scan over the whole quarter.
        builder
            .Entity<InstitutionalHolding>()
            .HasIndex(h => new
            {
                h.ReportDate,
                h.CommonStockId,
                h.InstitutionalHolderId,
            })
            .IncludeProperties(h => new { h.Shares, h.Value });

        // Covering index for the per-holder 13F ranking pages (AUM Movers,
        // Top by AUM, Double-Down): WHERE ReportDate IN (<quarter>[, <prior>])
        // GROUP BY InstitutionalHolderId with COUNT(DISTINCT CommonStockId) and
        // SUM(Shares)/SUM(Value). Mirror of the per-stock ranking index above but
        // with the holder as the group key, so the same quarter-filtered scan runs
        // index-only with no sort. The InstitutionalHolderId-leading covering index
        // higher up can't serve it — it can't seek the ReportDate filter.
        builder
            .Entity<InstitutionalHolding>()
            .HasIndex(h => new
            {
                h.ReportDate,
                h.InstitutionalHolderId,
                h.CommonStockId,
            })
            .IncludeProperties(h => new { h.Shares, h.Value });

        builder.Entity<ProcessedDataSet>();
        builder.Entity<ProcessedFiling>();
        builder.Entity<InstitutionalFiling>();
        builder.Entity<RealtimeSweepState>();
        builder.Entity<FundScore>();

        // AumQuarterlySnapshot uses ReportDate as the primary key. The [Key]
        // attribute can't be paired with [DatabaseGenerated(None)] without EF
        // also treating it as identity-by-convention on integral types, but the
        // intent is identical here — DateOnly key, caller-supplied. Configured
        // via Fluent API to keep the entity declaration attribute-only.
        builder.Entity<AumQuarterlySnapshot>().HasKey(s => s.ReportDate);

        // Partial index on DirtyAt — in steady state ~99% of rows have
        // DirtyAt = NULL, so a full btree wastes space and slows the drain
        // worker's "WHERE DirtyAt IS NOT NULL AND DirtyAt < cutoff" scan.
        // The [Index] attribute cannot express the HasFilter predicate, so
        // this overrides the attribute-declared index in AumQuarterlySnapshot.
        builder
            .Entity<AumQuarterlySnapshot>()
            .HasIndex(s => s.DirtyAt)
            .HasFilter("\"DirtyAt\" IS NOT NULL");

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

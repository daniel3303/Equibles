using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <summary>
    /// Widens the TransactionDate covering index with the four columns the insider-sentiment
    /// gate reads (CommonStockId, InsiderOwnerId, TransactionCode, IsRule10b5One) so its
    /// 90-day window resolves as an index-only scan instead of heap-fetching every candidate
    /// row. Measured on production: 66,497 buffers down to 2,511, zero heap fetches.
    ///
    /// Raw SQL rather than the scaffolded DropIndex/CreateIndex pair, for three reasons:
    /// CONCURRENTLY (suppressTransaction) so neither build takes a write lock on the live
    /// table — the Form 4 scraper writes continuously; IF NOT EXISTS so a deployment that
    /// pre-built the index by hand is a no-op; and create-before-drop so no window exists in
    /// which a TransactionDate scan has no covering index to use. The scaffolded version
    /// dropped first and rebuilt 361 MB under ACCESS EXCLUSIVE inside the deploy's migrate
    /// gate.
    ///
    /// An index-only scan is only reachable while the visibility map is fresh, and nothing was
    /// keeping it so: at ~1k inserts/day against 3.16M rows, the server-wide
    /// autovacuum_vacuum_insert_scale_factor of 0.2 puts the next insert-triggered vacuum
    /// ~630 days out, and the update path's 0.2 left a bulk reprocess pass's 512k dead tuples
    /// parked below the 633k trigger indefinitely. Production sat at 46.5% all-visible pages,
    /// which is why the planner refused an index-only scan even once the index existed. The
    /// table-level overrides below pin both paths to this table's actual churn; a full vacuum
    /// pass costs 7.7s.
    /// </summary>
    public partial class WidenInsiderTransactionCoveringIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // A failed CONCURRENTLY build leaves an INVALID index behind, which IF NOT EXISTS
            // would then treat as "exists" forever — drop such a leftover first (an invalid
            // index serves no queries, so the plain drop's brief lock is free), then build.
            migrationBuilder.Sql(
                "DO $$ BEGIN "
                    + "IF EXISTS (SELECT 1 FROM pg_class c JOIN pg_index i ON i.indexrelid = c.oid "
                    + "WHERE c.relname = 'IX_InsiderTransaction_TransactionDate_Covering' AND NOT i.indisvalid) THEN "
                    + "EXECUTE 'DROP INDEX \"IX_InsiderTransaction_TransactionDate_Covering\"'; END IF; END $$;",
                suppressTransaction: true
            );
            migrationBuilder.Sql(
                "CREATE INDEX CONCURRENTLY IF NOT EXISTS \"IX_InsiderTransaction_TransactionDate_Covering\" "
                    + "ON \"InsiderTransaction\" (\"TransactionDate\") "
                    + "INCLUDE (\"Shares\", \"PricePerShare\", \"IsPriceValid\", \"SecurityKind\", "
                    + "\"SecurityTitle\", \"CommonStockId\", \"InsiderOwnerId\", \"TransactionCode\", "
                    + "\"IsRule10b5One\");",
                suppressTransaction: true
            );
            // Only now that the replacement is live and valid: the old five-column index is a
            // strict subset (same key column, fewer INCLUDE columns), so every query it served
            // is served by the new one.
            migrationBuilder.Sql(
                "DROP INDEX CONCURRENTLY IF EXISTS \"IX_InsiderTransaction_TransactionDate\";",
                suppressTransaction: true
            );
            // Insert-triggered vacuum on a fixed row count rather than a fraction of a
            // 3.16M-row table — this is what keeps the visibility map fresh, and the newest
            // rows are exactly the ones every 90-day window scan reads.
            migrationBuilder.Sql(
                "ALTER TABLE \"InsiderTransaction\" SET ("
                    + "autovacuum_vacuum_insert_threshold = 5000, "
                    + "autovacuum_vacuum_insert_scale_factor = 0, "
                    + "autovacuum_vacuum_scale_factor = 0.02);"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TABLE \"InsiderTransaction\" RESET ("
                    + "autovacuum_vacuum_insert_threshold, "
                    + "autovacuum_vacuum_insert_scale_factor, "
                    + "autovacuum_vacuum_scale_factor);"
            );
            migrationBuilder.Sql(
                "CREATE INDEX CONCURRENTLY IF NOT EXISTS \"IX_InsiderTransaction_TransactionDate\" "
                    + "ON \"InsiderTransaction\" (\"TransactionDate\") "
                    + "INCLUDE (\"Shares\", \"PricePerShare\", \"IsPriceValid\", \"SecurityKind\", "
                    + "\"SecurityTitle\");",
                suppressTransaction: true
            );
            migrationBuilder.Sql(
                "DROP INDEX CONCURRENTLY IF EXISTS \"IX_InsiderTransaction_TransactionDate_Covering\";",
                suppressTransaction: true
            );
        }
    }
}

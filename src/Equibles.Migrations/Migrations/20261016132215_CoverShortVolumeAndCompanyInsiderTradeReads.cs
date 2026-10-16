using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <summary>
    /// Replaces the key-only (EquityListingId, Date) short-volume index and the key-only
    /// (EquityIssuerId, TransactionDate) insider-trade index with covering versions, so a
    /// listing's short-volume history and a company's trades resolve as index-only scans. On
    /// production those reads touched about a thousand and about six hundred heap pages per
    /// stock, one per row.
    ///
    /// Raw SQL, like WidenInsiderTransactionCoveringIndex: CONCURRENTLY so neither build locks
    /// the live tables against the FINRA and Form 4 writers, IF NOT EXISTS so a hand-built index
    /// is a no-op, and create-before-drop so the unique (listing, date) key and the per-company
    /// date lookup are never without an index.
    ///
    /// DailyShortVolume also gets an insert-driven vacuum: it takes about 12k rows a day against
    /// 14M, so the server-wide insert scale factor of 0.2 almost never vacuums it and production
    /// sat at 71% all-visible pages, which keeps the planner off an index-only scan.
    /// </summary>
    public partial class CoverShortVolumeAndCompanyInsiderTradeReads : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // A failed CONCURRENTLY build leaves an INVALID index that IF NOT EXISTS would treat
            // as built, so drop such a leftover first.
            DropInvalidLeftover(migrationBuilder, "IX_DailyShortVolume_EquityListingId_Date_Covering");
            migrationBuilder.Sql(
                "CREATE UNIQUE INDEX CONCURRENTLY IF NOT EXISTS \"IX_DailyShortVolume_EquityListingId_Date_Covering\" "
                    + "ON \"DailyShortVolume\" (\"EquityListingId\", \"Date\") "
                    + "INCLUDE (\"ShortVolume\", \"ShortExemptVolume\", \"TotalVolume\");",
                suppressTransaction: true
            );
            migrationBuilder.Sql(
                "DROP INDEX CONCURRENTLY IF EXISTS \"IX_DailyShortVolume_EquityListingId_Date\";",
                suppressTransaction: true
            );

            DropInvalidLeftover(migrationBuilder, "IX_InsiderTransaction_EquityIssuerId_TransactionDate_Covering");
            migrationBuilder.Sql(
                "CREATE INDEX CONCURRENTLY IF NOT EXISTS \"IX_InsiderTransaction_EquityIssuerId_TransactionDate_Covering\" "
                    + "ON \"InsiderTransaction\" (\"EquityIssuerId\", \"TransactionDate\") "
                    + "INCLUDE (\"FilingDate\", \"TransactionCode\", \"Shares\", \"PricePerShare\", "
                    + "\"InsiderOwnerId\", \"IsRule10b5One\", \"IsPriceValid\", \"SecurityKind\", \"SecurityTitle\");",
                suppressTransaction: true
            );
            migrationBuilder.Sql(
                "DROP INDEX CONCURRENTLY IF EXISTS \"IX_InsiderTransaction_EquityIssuerId_TransactionDate\";",
                suppressTransaction: true
            );

            migrationBuilder.Sql(
                "ALTER TABLE \"DailyShortVolume\" SET ("
                    + "autovacuum_vacuum_insert_threshold = 10000, "
                    + "autovacuum_vacuum_insert_scale_factor = 0, "
                    + "autovacuum_vacuum_scale_factor = 0.02);"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TABLE \"DailyShortVolume\" RESET ("
                    + "autovacuum_vacuum_insert_threshold, "
                    + "autovacuum_vacuum_insert_scale_factor, "
                    + "autovacuum_vacuum_scale_factor);"
            );

            DropInvalidLeftover(migrationBuilder, "IX_InsiderTransaction_EquityIssuerId_TransactionDate");
            migrationBuilder.Sql(
                "CREATE INDEX CONCURRENTLY IF NOT EXISTS \"IX_InsiderTransaction_EquityIssuerId_TransactionDate\" "
                    + "ON \"InsiderTransaction\" (\"EquityIssuerId\", \"TransactionDate\");",
                suppressTransaction: true
            );
            migrationBuilder.Sql(
                "DROP INDEX CONCURRENTLY IF EXISTS \"IX_InsiderTransaction_EquityIssuerId_TransactionDate_Covering\";",
                suppressTransaction: true
            );

            DropInvalidLeftover(migrationBuilder, "IX_DailyShortVolume_EquityListingId_Date");
            migrationBuilder.Sql(
                "CREATE UNIQUE INDEX CONCURRENTLY IF NOT EXISTS \"IX_DailyShortVolume_EquityListingId_Date\" "
                    + "ON \"DailyShortVolume\" (\"EquityListingId\", \"Date\");",
                suppressTransaction: true
            );
            migrationBuilder.Sql(
                "DROP INDEX CONCURRENTLY IF EXISTS \"IX_DailyShortVolume_EquityListingId_Date_Covering\";",
                suppressTransaction: true
            );
        }

        private static void DropInvalidLeftover(MigrationBuilder migrationBuilder, string indexName)
        {
            migrationBuilder.Sql(
                "DO $$ BEGIN "
                    + "IF EXISTS (SELECT 1 FROM pg_class c JOIN pg_index i ON i.indexrelid = c.oid "
                    + $"WHERE c.relname = '{indexName}' AND NOT i.indisvalid) THEN "
                    + $"EXECUTE 'DROP INDEX \"{indexName}\"'; END IF; END $$;",
                suppressTransaction: true
            );
        }
    }
}

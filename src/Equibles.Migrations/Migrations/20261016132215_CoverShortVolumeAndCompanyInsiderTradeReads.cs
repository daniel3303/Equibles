using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <summary>
    /// Replaces the key-only short-volume (listing, date) and insider-trade (company, date) indexes with
    /// covering ones, built concurrently before each drop so uniqueness and the lookup never lapse.
    /// DailyShortVolume gets an insert-driven vacuum so its visibility map stays fresh enough for
    /// index-only scans.
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

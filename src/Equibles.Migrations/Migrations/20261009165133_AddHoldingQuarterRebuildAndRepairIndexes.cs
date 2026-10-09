using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <summary>
    /// Replaces the quarter-leading holder index with one that serves every quarter-rebuild read
    /// index-only, and adds the partial Id worklists of the filed-revise and unmarked-zero repairs.
    /// Every build and drop runs concurrently, dropping only an invalid leftover first and the old
    /// index only after its replacement exists.
    /// </summary>
    public partial class AddHoldingQuarterRebuildAndRepairIndexes : Migration
    {
        private const string OldQuarterIndex =
            "IX_InstitutionalHolding_ReportDate_InstitutionalHolderId_Equit~";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            DropInvalidLeftover(migrationBuilder, "IX_InstitutionalHolding_QuarterRebuild");
            migrationBuilder.Sql(
                "CREATE INDEX CONCURRENTLY IF NOT EXISTS \"IX_InstitutionalHolding_QuarterRebuild\" "
                    + "ON \"InstitutionalHolding\" (\"ReportDate\", \"InstitutionalHolderId\", \"EquityIssuerId\") "
                    + "INCLUDE (\"Shares\", \"Value\", \"FilingType\", \"FilingDate\", \"AccessionNumber\", \"ListedTicker\");",
                suppressTransaction: true
            );
            migrationBuilder.Sql(
                $"DROP INDEX CONCURRENTLY IF EXISTS \"{OldQuarterIndex}\";",
                suppressTransaction: true
            );

            DropInvalidLeftover(migrationBuilder, "IX_InstitutionalHolding_FiledReviseRepair");
            migrationBuilder.Sql(
                "CREATE INDEX CONCURRENTLY IF NOT EXISTS \"IX_InstitutionalHolding_FiledReviseRepair\" "
                    + "ON \"InstitutionalHolding\" (\"Id\") "
                    + "WHERE NOT \"ValuePending\" AND \"ShareType\" = 0 AND NOT \"ValueUnavailable\" "
                    + "AND \"ValueSource\" = 1 "
                    + "AND \"FiledValue\" IS NOT NULL AND \"FiledValue\" > 0 "
                    + "AND \"Value\" = \"FiledValue\" AND \"Shares\" > 0;",
                suppressTransaction: true
            );

            DropInvalidLeftover(migrationBuilder, "IX_InstitutionalHolding_UnmarkedZeroRepair");
            migrationBuilder.Sql(
                "CREATE INDEX CONCURRENTLY IF NOT EXISTS \"IX_InstitutionalHolding_UnmarkedZeroRepair\" "
                    + "ON \"InstitutionalHolding\" (\"Id\") "
                    + "WHERE \"Value\" = 0 AND NOT \"ValuePending\" AND NOT \"ValueUnavailable\" "
                    + "AND (\"FiledValue\" IS NULL OR \"FiledValue\" <= 0);",
                suppressTransaction: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            DropInvalidLeftover(migrationBuilder, OldQuarterIndex);
            migrationBuilder.Sql(
                $"CREATE INDEX CONCURRENTLY IF NOT EXISTS \"{OldQuarterIndex}\" "
                    + "ON \"InstitutionalHolding\" (\"ReportDate\", \"InstitutionalHolderId\", \"EquityIssuerId\") "
                    + "INCLUDE (\"Shares\", \"Value\");",
                suppressTransaction: true
            );
            migrationBuilder.Sql(
                "DROP INDEX CONCURRENTLY IF EXISTS \"IX_InstitutionalHolding_QuarterRebuild\";",
                suppressTransaction: true
            );
            migrationBuilder.Sql(
                "DROP INDEX CONCURRENTLY IF EXISTS \"IX_InstitutionalHolding_FiledReviseRepair\";",
                suppressTransaction: true
            );
            migrationBuilder.Sql(
                "DROP INDEX CONCURRENTLY IF EXISTS \"IX_InstitutionalHolding_UnmarkedZeroRepair\";",
                suppressTransaction: true
            );
        }

        // An interrupted CONCURRENTLY build leaves an invalid index that IF NOT EXISTS would keep.
        private static void DropInvalidLeftover(MigrationBuilder migrationBuilder, string name) =>
            migrationBuilder.Sql(
                "DO $$ BEGIN "
                    + "IF EXISTS (SELECT 1 FROM pg_class c JOIN pg_index i ON i.indexrelid = c.oid "
                    + $"WHERE c.relname = '{name}' AND NOT i.indisvalid) THEN "
                    + $"EXECUTE 'DROP INDEX \"{name}\"'; END IF; END $$;",
                suppressTransaction: true
            );
    }
}

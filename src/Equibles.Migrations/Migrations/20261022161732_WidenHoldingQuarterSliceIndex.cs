using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <summary>
    /// Replaces the quarter-rebuild index with one that also carries the share, option and
    /// value-state columns, so the holder-intelligence rebuild's filer-batch loads and aggregate
    /// benchmark read a quarter index-only instead of fetching a heap page per position. The build
    /// and the drop run concurrently, dropping only an invalid leftover first and the old index
    /// only after its replacement exists.
    /// </summary>
    public partial class WidenHoldingQuarterSliceIndex : Migration
    {
        private const string OldIndex = "IX_InstitutionalHolding_QuarterRebuild";
        private const string NewIndex = "IX_InstitutionalHolding_QuarterSlice";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            DropInvalidLeftover(migrationBuilder, NewIndex);
            migrationBuilder.Sql(
                $"CREATE INDEX CONCURRENTLY IF NOT EXISTS \"{NewIndex}\" "
                    + "ON \"InstitutionalHolding\" (\"ReportDate\", \"InstitutionalHolderId\", \"EquityIssuerId\") "
                    + "INCLUDE (\"Shares\", \"Value\", \"FilingType\", \"FilingDate\", \"AccessionNumber\", \"ListedTicker\", "
                    + "\"ShareType\", \"OptionType\", \"FiledValue\", \"ValuePending\", \"ValueUnavailable\");",
                suppressTransaction: true
            );
            migrationBuilder.Sql(
                $"DROP INDEX CONCURRENTLY IF EXISTS \"{OldIndex}\";",
                suppressTransaction: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            DropInvalidLeftover(migrationBuilder, OldIndex);
            migrationBuilder.Sql(
                $"CREATE INDEX CONCURRENTLY IF NOT EXISTS \"{OldIndex}\" "
                    + "ON \"InstitutionalHolding\" (\"ReportDate\", \"InstitutionalHolderId\", \"EquityIssuerId\") "
                    + "INCLUDE (\"Shares\", \"Value\", \"FilingType\", \"FilingDate\", \"AccessionNumber\", \"ListedTicker\");",
                suppressTransaction: true
            );
            migrationBuilder.Sql(
                $"DROP INDEX CONCURRENTLY IF EXISTS \"{NewIndex}\";",
                suppressTransaction: true
            );
        }

        // An interrupted CONCURRENTLY build leaves an invalid index that IF NOT EXISTS would keep.
        // Renaming it aside takes only SHARE UPDATE EXCLUSIVE, where a plain DROP would queue every
        // reader and writer of the table behind an ACCESS EXCLUSIVE lock. A build still running
        // under that name is also invalid, so the migration refuses instead of discarding it.
        private static void DropInvalidLeftover(MigrationBuilder migrationBuilder, string name)
        {
            migrationBuilder.Sql(
                "DO $$ BEGIN "
                    + "IF EXISTS (SELECT 1 FROM pg_stat_progress_create_index p "
                    + $"JOIN pg_class c ON c.oid = p.index_relid WHERE c.relname = '{name}') THEN "
                    + $"RAISE EXCEPTION '{name} is still being built; migrate once the build finishes'; END IF; "
                    + "IF EXISTS (SELECT 1 FROM pg_class c JOIN pg_index i ON i.indexrelid = c.oid "
                    + $"WHERE c.relname = '{name}' AND NOT i.indisvalid) THEN "
                    + $"EXECUTE 'ALTER INDEX \"{name}\" RENAME TO \"{name}_inv\"'; END IF; END $$;",
                suppressTransaction: true
            );
            migrationBuilder.Sql(
                $"DROP INDEX CONCURRENTLY IF EXISTS \"{name}_inv\";",
                suppressTransaction: true
            );
        }
    }
}

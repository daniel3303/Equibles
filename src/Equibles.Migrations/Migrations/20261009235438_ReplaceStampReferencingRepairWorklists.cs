using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <summary>
    /// Replaces a filed-revise or unmarked-zero worklist whose predicate still references a
    /// retry-stamp column (the first published AddHoldingQuarterRebuildAndRepairIndexes built
    /// them that way) with the stamp-free predicate the model declares. A stale index is renamed
    /// aside, the replacement is built concurrently under the canonical name, and the stale copy
    /// is dropped concurrently; a database that already carries the stamp-free predicates is left
    /// untouched.
    /// </summary>
    public partial class ReplaceStampReferencingRepairWorklists : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            Replace(
                migrationBuilder,
                "IX_InstitutionalHolding_FiledReviseRepair",
                "ValueLastRetryAt",
                "ON \"InstitutionalHolding\" (\"Id\") "
                    + "WHERE NOT \"ValuePending\" AND \"ShareType\" = 0 AND NOT \"ValueUnavailable\" "
                    + "AND \"ValueSource\" = 1 "
                    + "AND \"FiledValue\" IS NOT NULL AND \"FiledValue\" > 0 "
                    + "AND \"Value\" = \"FiledValue\" AND \"Shares\" > 0"
            );
            Replace(
                migrationBuilder,
                "IX_InstitutionalHolding_UnmarkedZeroRepair",
                "ValueRetryCount",
                "ON \"InstitutionalHolding\" (\"Id\") "
                    + "WHERE \"Value\" = 0 AND NOT \"ValuePending\" AND NOT \"ValueUnavailable\" "
                    + "AND (\"FiledValue\" IS NULL OR \"FiledValue\" <= 0)"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The stamp-free predicates are the model's; reverting would rebuild the HOT-blocking
            // ones, so the previous migration's Down owns dropping these indexes.
        }

        private static void Replace(
            MigrationBuilder migrationBuilder,
            string name,
            string staleColumn,
            string definition
        )
        {
            var stale = name + "_stale";
            // Renaming needs only a SHARE UPDATE EXCLUSIVE lock, so writers keep running while
            // the replacement builds. Only a predicate that names the stamp column is set aside.
            migrationBuilder.Sql(
                "DO $$ BEGIN "
                    + "IF EXISTS (SELECT 1 FROM pg_class c JOIN pg_index i ON i.indexrelid = c.oid "
                    + $"WHERE c.relname = '{name}' AND pg_get_indexdef(c.oid) LIKE '%\"{staleColumn}\"%') THEN "
                    + $"EXECUTE 'ALTER INDEX \"{name}\" RENAME TO \"{stale}\"'; END IF; END $$;",
                suppressTransaction: true
            );
            // An interrupted CONCURRENTLY build leaves an invalid index that IF NOT EXISTS would keep.
            migrationBuilder.Sql(
                "DO $$ BEGIN "
                    + "IF EXISTS (SELECT 1 FROM pg_class c JOIN pg_index i ON i.indexrelid = c.oid "
                    + $"WHERE c.relname = '{name}' AND NOT i.indisvalid) THEN "
                    + $"EXECUTE 'DROP INDEX \"{name}\"'; END IF; END $$;",
                suppressTransaction: true
            );
            migrationBuilder.Sql(
                $"CREATE INDEX CONCURRENTLY IF NOT EXISTS \"{name}\" {definition};",
                suppressTransaction: true
            );
            migrationBuilder.Sql(
                $"DROP INDEX CONCURRENTLY IF EXISTS \"{stale}\";",
                suppressTransaction: true
            );
        }
    }
}

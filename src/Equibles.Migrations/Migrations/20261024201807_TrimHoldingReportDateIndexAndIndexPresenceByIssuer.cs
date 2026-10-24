using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <summary>
    /// Drops the plain ReportDate index, whose reads the ReportDate-leading quarter-slice index
    /// serves, and indexes holder presence by issuer for the per-security holder reads. The build
    /// and the drop run concurrently, dropping only an invalid leftover first.
    /// </summary>
    public partial class TrimHoldingReportDateIndexAndIndexPresenceByIssuer : Migration
    {
        private const string DroppedIndex = "IX_InstitutionalHolding_ReportDate";
        private const string PresenceIndex = "IX_HolderStockPresence_EquityIssuerId";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            DropInvalidLeftover(migrationBuilder, PresenceIndex);
            migrationBuilder.Sql(
                $"CREATE INDEX CONCURRENTLY IF NOT EXISTS \"{PresenceIndex}\" "
                    + "ON \"HolderStockPresence\" (\"EquityIssuerId\");",
                suppressTransaction: true
            );
            migrationBuilder.Sql(
                $"DROP INDEX CONCURRENTLY IF EXISTS \"{DroppedIndex}\";",
                suppressTransaction: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            DropInvalidLeftover(migrationBuilder, DroppedIndex);
            migrationBuilder.Sql(
                $"CREATE INDEX CONCURRENTLY IF NOT EXISTS \"{DroppedIndex}\" "
                    + "ON \"InstitutionalHolding\" (\"ReportDate\");",
                suppressTransaction: true
            );
            migrationBuilder.Sql(
                $"DROP INDEX CONCURRENTLY IF EXISTS \"{PresenceIndex}\";",
                suppressTransaction: true
            );
        }

        // An interrupted CONCURRENTLY build leaves an invalid index that IF NOT EXISTS would keep.
        // Renaming it aside takes only SHARE UPDATE EXCLUSIVE, where a plain DROP would queue every
        // reader and writer of the table behind an ACCESS EXCLUSIVE lock. A build still running
        // under that name is also invalid, so the migration refuses instead of discarding it. The
        // aside name is cut to fit PostgreSQL's 63-character identifier limit.
        private static void DropInvalidLeftover(MigrationBuilder migrationBuilder, string name)
        {
            var aside = name[..Math.Min(name.Length, 59)] + "_inv";
            migrationBuilder.Sql(
                "DO $$ BEGIN "
                    + "IF EXISTS (SELECT 1 FROM pg_stat_progress_create_index p "
                    + $"JOIN pg_class c ON c.oid = p.index_relid WHERE c.relname = '{name}') THEN "
                    + $"RAISE EXCEPTION '{name} is still being built; migrate once the build finishes'; END IF; "
                    + "IF EXISTS (SELECT 1 FROM pg_class c JOIN pg_index i ON i.indexrelid = c.oid "
                    + $"WHERE c.relname = '{name}' AND NOT i.indisvalid) THEN "
                    + $"EXECUTE 'ALTER INDEX \"{name}\" RENAME TO \"{aside}\"'; END IF; END $$;",
                suppressTransaction: true
            );
            migrationBuilder.Sql(
                $"DROP INDEX CONCURRENTLY IF EXISTS \"{aside}\";",
                suppressTransaction: true
            );
        }
    }
}

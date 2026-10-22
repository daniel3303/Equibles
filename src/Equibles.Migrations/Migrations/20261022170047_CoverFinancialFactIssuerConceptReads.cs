using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <summary>
    /// Replaces the issuer-concept-period index with one that also carries the columns issuer and
    /// concept fact reads select, so those reads stop fetching a heap page per fact. The build and the
    /// drop run concurrently, dropping only an invalid leftover first and the old index only after
    /// its replacement exists.
    /// </summary>
    public partial class CoverFinancialFactIssuerConceptReads : Migration
    {
        private const string OldIndex = "IX_FinancialFact_EquityIssuerId_FinancialConceptId_PeriodEnd";
        private const string NewIndex = "IX_FinancialFact_IssuerConceptPeriod";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            DropInvalidLeftover(migrationBuilder, NewIndex);
            migrationBuilder.Sql(
                $"CREATE INDEX CONCURRENTLY IF NOT EXISTS \"{NewIndex}\" "
                    + "ON \"FinancialFact\" (\"EquityIssuerId\", \"FinancialConceptId\", \"PeriodEnd\") "
                    + "INCLUDE (\"PeriodStart\", \"PeriodType\", \"FiscalYear\", \"FiscalPeriod\", \"Value\", \"Unit\", "
                    + "\"FiledDate\", \"Form\", \"DimensionsKey\", \"Id\");",
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
                    + "ON \"FinancialFact\" (\"EquityIssuerId\", \"FinancialConceptId\", \"PeriodEnd\");",
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

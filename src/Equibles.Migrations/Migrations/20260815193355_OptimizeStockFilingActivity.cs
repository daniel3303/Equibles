using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <summary>
    /// Adds the covering stock+filing-date index behind the portal's recent-filing aggregate
    /// (#6049). InstitutionalHolding contains tens of millions of rows, so the index is built
    /// concurrently and outside the migration transaction to keep live ingestion writable.
    /// The invalid-index cleanup makes a failed or interrupted concurrent build retry-safe.
    /// </summary>
    public partial class OptimizeStockFilingActivity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "DO $$ BEGIN "
                    + "IF EXISTS (SELECT 1 FROM pg_class c JOIN pg_index i ON i.indexrelid = c.oid "
                    + "WHERE c.relname = 'IX_InstitutionalHolding_CommonStockId_FilingDate' AND NOT i.indisvalid) THEN "
                    + "EXECUTE 'DROP INDEX \"IX_InstitutionalHolding_CommonStockId_FilingDate\"'; END IF; END $$;",
                suppressTransaction: true
            );
            migrationBuilder.Sql(
                "CREATE INDEX CONCURRENTLY IF NOT EXISTS \"IX_InstitutionalHolding_CommonStockId_FilingDate\" "
                    + "ON \"InstitutionalHolding\" (\"CommonStockId\", \"FilingDate\") "
                    + "INCLUDE (\"AccessionNumber\", \"InstitutionalHolderId\");",
                suppressTransaction: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "DROP INDEX CONCURRENTLY IF EXISTS \"IX_InstitutionalHolding_CommonStockId_FilingDate\";",
                suppressTransaction: true
            );
        }
    }
}

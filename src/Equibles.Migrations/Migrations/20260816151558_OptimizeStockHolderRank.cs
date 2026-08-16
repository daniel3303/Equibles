using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <summary>
    /// Adds the partial covering index behind the per-stock holder-rank aggregate (#6049).
    /// The holdings table contains tens of millions of rows, so the index is built
    /// concurrently and outside the migration transaction. Invalid-index cleanup makes an
    /// interrupted concurrent build retry-safe.
    /// </summary>
    public partial class OptimizeStockHolderRank : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "DO $$ BEGIN "
                    + "IF EXISTS (SELECT 1 FROM pg_class c JOIN pg_index i ON i.indexrelid = c.oid "
                    + "WHERE c.relname = 'IX_InstitutionalHolding_StockQuarterCommonValue' AND NOT i.indisvalid) THEN "
                    + "EXECUTE 'DROP INDEX \"IX_InstitutionalHolding_StockQuarterCommonValue\"'; END IF; END $$;",
                suppressTransaction: true
            );
            migrationBuilder.Sql(
                "CREATE INDEX CONCURRENTLY IF NOT EXISTS \"IX_InstitutionalHolding_StockQuarterCommonValue\" "
                    + "ON \"InstitutionalHolding\" (\"CommonStockId\", \"ReportDate\", \"InstitutionalHolderId\") "
                    + "INCLUDE (\"Value\") WHERE \"FilingType\" = 0 AND \"OptionType\" IS NULL;",
                suppressTransaction: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "DROP INDEX CONCURRENTLY IF EXISTS \"IX_InstitutionalHolding_StockQuarterCommonValue\";",
                suppressTransaction: true
            );
        }
    }
}

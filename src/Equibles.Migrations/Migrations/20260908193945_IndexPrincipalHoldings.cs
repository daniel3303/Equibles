using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <summary>Supports bounded principal-value repair without blocking holdings writes.</summary>
    public partial class IndexPrincipalHoldings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "DO $$ BEGIN "
                    + "IF EXISTS (SELECT 1 FROM pg_class c JOIN pg_index i ON i.indexrelid = c.oid "
                    + "WHERE c.relname = 'IX_InstitutionalHolding_Principal' AND NOT i.indisvalid) THEN "
                    + "EXECUTE 'DROP INDEX \"IX_InstitutionalHolding_Principal\"'; END IF; END $$;",
                suppressTransaction: true
            );
            migrationBuilder.Sql(
                "CREATE INDEX CONCURRENTLY IF NOT EXISTS \"IX_InstitutionalHolding_Principal\" "
                    + "ON \"InstitutionalHolding\" (\"ShareType\", \"Id\") WHERE \"ShareType\" = 1;",
                suppressTransaction: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "DROP INDEX CONCURRENTLY IF EXISTS \"IX_InstitutionalHolding_Principal\";",
                suppressTransaction: true
            );
        }
    }
}

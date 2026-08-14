using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <summary>
    /// Adds the composite index that lets the insider reprocess drain seek one exact parser
    /// version and stream distinct accessions into its batch limit (#4374). The build is
    /// restart-safe because a failed concurrent build leaves an invalid index behind, while a
    /// crash after a successful build can occur before EF records the migration as applied.
    /// </summary>
    public partial class OptimizeInsiderReprocessSelection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "DO $$ BEGIN "
                    + "IF EXISTS (SELECT 1 FROM pg_class c JOIN pg_index i ON i.indexrelid = c.oid "
                    + "WHERE c.relname = 'IX_InsiderTransaction_ParserVersion_AccessionNumber' AND NOT i.indisvalid) THEN "
                    + "EXECUTE 'DROP INDEX \"IX_InsiderTransaction_ParserVersion_AccessionNumber\"'; END IF; END $$;",
                suppressTransaction: true
            );
            migrationBuilder.Sql(
                "CREATE INDEX CONCURRENTLY IF NOT EXISTS \"IX_InsiderTransaction_ParserVersion_AccessionNumber\" "
                    + "ON \"InsiderTransaction\" (\"ParserVersion\", \"AccessionNumber\");",
                suppressTransaction: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "DROP INDEX CONCURRENTLY IF EXISTS \"IX_InsiderTransaction_ParserVersion_AccessionNumber\";",
                suppressTransaction: true
            );
        }
    }
}

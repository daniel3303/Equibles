using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddXbrlRecaptureScanIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // A concurrent build that is interrupted (restart, cancelled statement, or the host's
            // migration timeout) leaves an invalid index behind, and every later attempt then
            // fails with "relation already exists". Drop the leftover first - the same guard the
            // holding repair indexes use.
            migrationBuilder.Sql(
                "DO $$ BEGIN "
                    + "IF EXISTS (SELECT 1 FROM pg_class c JOIN pg_index i ON i.indexrelid = c.oid "
                    + "WHERE c.relname = 'IX_Document_XbrlRecapture' AND NOT i.indisvalid) THEN "
                    + "EXECUTE 'DROP INDEX ' || quote_ident('IX_Document_XbrlRecapture'); END IF; "
                    + "END $$;",
                suppressTransaction: true
            );

            migrationBuilder
                .CreateIndex(
                name: "IX_Document_XbrlRecapture",
                table: "Document",
                columns: new[] { "ReportingDate", "Id" },
                descending: new[] { true, false },
                filter: "\"XbrlStatus\" = 1 AND \"XbrlType\" = 0 AND \"XbrlContentId\" IS NOT NULL AND \"XbrlCaptureAttempts\" < 5")
                .Annotation("Npgsql:CreatedConcurrently", true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Document_XbrlRecapture",
                table: "Document");
        }
    }
}

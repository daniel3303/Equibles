using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class EsefEmptyReportAttemptsReset : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Once: European reports parked by earlier builders get their attempts back for
            // generation 7. Their failures were the heading step running out of memory on JSON
            // notes and readable reports rebuilt empty past the conversion limit, both fixed in it.
            migrationBuilder.Sql(
                """
                UPDATE "Document" SET "NormalizedContentAttempts" = 0
                WHERE "DocumentType" IN ('EsefAnnualReport', 'EsefReport')
                  AND "NormalizedContentVersion" < 7
                  AND "NormalizedContentAttempts" > 0;
                """
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}

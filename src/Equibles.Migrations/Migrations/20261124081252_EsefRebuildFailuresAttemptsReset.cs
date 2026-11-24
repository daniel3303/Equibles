using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class EsefRebuildFailuresAttemptsReset : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Once: European reports that failed generation 7 on a stylesheet rule with no readable
            // selector, or on a note holding no visible text, get their attempts back; both are fixed.
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

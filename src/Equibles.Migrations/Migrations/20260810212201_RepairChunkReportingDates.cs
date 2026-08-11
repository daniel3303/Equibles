using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class RepairChunkReportingDates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE "Chunk" AS c
                SET "ReportingDate" = (d."ReportingDate"::timestamp AT TIME ZONE 'UTC')
                FROM "Document" AS d
                WHERE c."DocumentId" = d."Id"
                  AND c."ReportingDate" IS DISTINCT FROM
                      (d."ReportingDate"::timestamp AT TIME ZONE 'UTC');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The incorrect denormalized dates cannot be reconstructed safely.
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class NormalizeStoredDocumentTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "NormalizedContentAttempts",
                table: "Document",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "NormalizedContentVersion",
                table: "Document",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_Document_NormalizationBackfill",
                table: "Document",
                columns: new[] { "DocumentType", "NormalizedContentVersion", "NormalizedContentAttempts", "ReportingDate", "Id" },
                descending: new[] { false, false, false, true, false });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Document_NormalizationBackfill",
                table: "Document");

            migrationBuilder.DropColumn(
                name: "NormalizedContentAttempts",
                table: "Document");

            migrationBuilder.DropColumn(
                name: "NormalizedContentVersion",
                table: "Document");
        }
    }
}

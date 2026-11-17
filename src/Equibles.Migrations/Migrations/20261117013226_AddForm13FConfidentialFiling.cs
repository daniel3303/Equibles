using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddForm13FConfidentialFiling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Form13FConfidentialFiling",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccessionNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    FilerCik = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ReportDate = table.Column<DateOnly>(type: "date", nullable: false),
                    FilingDate = table.Column<DateOnly>(type: "date", nullable: false),
                    IsAmendment = table.Column<bool>(type: "boolean", nullable: false),
                    DeclaredEntryTotal = table.Column<int>(type: "integer", nullable: true),
                    DisclosedPositionCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Form13FConfidentialFiling", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Form13FConfidentialFiling_AccessionNumber",
                table: "Form13FConfidentialFiling",
                column: "AccessionNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Form13FConfidentialFiling_FilerCik_ReportDate",
                table: "Form13FConfidentialFiling",
                columns: new[] { "FilerCik", "ReportDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Form13FConfidentialFiling");
        }
    }
}

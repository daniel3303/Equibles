using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddInstitutionalFilingSortIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_InstitutionalFiling_FilingDate",
                table: "InstitutionalFiling");

            migrationBuilder.CreateIndex(
                name: "IX_InstitutionalFiling_FilingDate_AccessionNumber",
                table: "InstitutionalFiling",
                columns: new[] { "FilingDate", "AccessionNumber" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_InstitutionalFiling_FilingDate_AccessionNumber",
                table: "InstitutionalFiling");

            migrationBuilder.CreateIndex(
                name: "IX_InstitutionalFiling_FilingDate",
                table: "InstitutionalFiling",
                column: "FilingDate");
        }
    }
}

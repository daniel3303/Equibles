using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddFilingDateToHolderCoveringIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_InstitutionalHolding_InstitutionalHolderId_ReportDate",
                table: "InstitutionalHolding");

            migrationBuilder.CreateIndex(
                name: "IX_InstitutionalHolding_InstitutionalHolderId_ReportDate",
                table: "InstitutionalHolding",
                columns: new[] { "InstitutionalHolderId", "ReportDate" })
                .Annotation("Npgsql:IndexInclude", new[] { "CommonStockId", "Value", "Shares", "FilingDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_InstitutionalHolding_InstitutionalHolderId_ReportDate",
                table: "InstitutionalHolding");

            migrationBuilder.CreateIndex(
                name: "IX_InstitutionalHolding_InstitutionalHolderId_ReportDate",
                table: "InstitutionalHolding",
                columns: new[] { "InstitutionalHolderId", "ReportDate" })
                .Annotation("Npgsql:IndexInclude", new[] { "CommonStockId", "Value", "Shares" });
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class HoldingsTrendCoveringIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_InstitutionalHolding_CommonStockId_ReportDate",
                table: "InstitutionalHolding"
            );

            migrationBuilder
                .CreateIndex(
                    name: "IX_InstitutionalHolding_CommonStockId_ReportDate",
                    table: "InstitutionalHolding",
                    columns: new[] { "CommonStockId", "ReportDate" }
                )
                .Annotation(
                    "Npgsql:IndexInclude",
                    new[] { "InstitutionalHolderId", "Value", "Shares" }
                );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_InstitutionalHolding_CommonStockId_ReportDate",
                table: "InstitutionalHolding"
            );

            migrationBuilder.CreateIndex(
                name: "IX_InstitutionalHolding_CommonStockId_ReportDate",
                table: "InstitutionalHolding",
                columns: new[] { "CommonStockId", "ReportDate" }
            );
        }
    }
}

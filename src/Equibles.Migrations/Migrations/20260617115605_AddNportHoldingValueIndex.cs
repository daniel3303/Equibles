using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddNportHoldingValueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_NportHolding_NportFilingId",
                table: "NportHolding");

            migrationBuilder.CreateIndex(
                name: "IX_NportHolding_NportFilingId_ValueUsd",
                table: "NportHolding",
                columns: new[] { "NportFilingId", "ValueUsd" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_NportHolding_NportFilingId_ValueUsd",
                table: "NportHolding");

            migrationBuilder.CreateIndex(
                name: "IX_NportHolding_NportFilingId",
                table: "NportHolding",
                column: "NportFilingId");
        }
    }
}

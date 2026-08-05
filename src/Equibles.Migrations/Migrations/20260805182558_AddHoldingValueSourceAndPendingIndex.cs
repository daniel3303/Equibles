using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddHoldingValueSourceAndPendingIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ValueSource",
                table: "InstitutionalHolding",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_InstitutionalHolding_ValuePending_Pairs",
                table: "InstitutionalHolding",
                columns: new[] { "CommonStockId", "ListedTicker", "ReportDate" },
                filter: "\"ValuePending\"");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_InstitutionalHolding_ValuePending_Pairs",
                table: "InstitutionalHolding");

            migrationBuilder.DropColumn(
                name: "ValueSource",
                table: "InstitutionalHolding");
        }
    }
}

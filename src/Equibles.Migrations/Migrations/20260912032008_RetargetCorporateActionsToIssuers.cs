using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class RetargetCorporateActionsToIssuers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CashDividend_CommonStock_CommonStockId",
                table: "CashDividend");

            migrationBuilder.DropForeignKey(
                name: "FK_StockSplit_CommonStock_CommonStockId",
                table: "StockSplit");

            migrationBuilder.AddForeignKey(
                name: "FK_CashDividend_EquityIssuer_CommonStockId",
                table: "CashDividend",
                column: "CommonStockId",
                principalTable: "EquityIssuer",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_StockSplit_EquityIssuer_CommonStockId",
                table: "StockSplit",
                column: "CommonStockId",
                principalTable: "EquityIssuer",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("Returning corporate actions to a legacy owner could orphan native issuer data.");
        }
    }
}

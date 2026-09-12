using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class RetargetCongressionalTradesToIssuers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CongressionalTrade_CommonStock_CommonStockId",
                table: "CongressionalTrade");

            migrationBuilder.AddForeignKey(
                name: "FK_CongressionalTrade_EquityIssuer_CommonStockId",
                table: "CongressionalTrade",
                column: "CommonStockId",
                principalTable: "EquityIssuer",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("Returning trades to a legacy owner could lose native issuer associations.");
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class RetargetDirectoryIdentityEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CommonStockCusipAlias_CommonStock_CommonStockId",
                table: "CommonStockCusipAlias");

            migrationBuilder.DropForeignKey(
                name: "FK_CommonStockDelistedListing_CommonStock_CommonStockId",
                table: "CommonStockDelistedListing");

            migrationBuilder.DropForeignKey(
                name: "FK_CommonStockListedCusip_CommonStock_CommonStockId",
                table: "CommonStockListedCusip");

            migrationBuilder.DropForeignKey(
                name: "FK_CommonStockTickerAlias_CommonStock_CommonStockId",
                table: "CommonStockTickerAlias");

            migrationBuilder.AddForeignKey(
                name: "FK_CommonStockCusipAlias_EquityIssuer_CommonStockId",
                table: "CommonStockCusipAlias",
                column: "CommonStockId",
                principalTable: "EquityIssuer",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_CommonStockDelistedListing_EquityIssuer_CommonStockId",
                table: "CommonStockDelistedListing",
                column: "CommonStockId",
                principalTable: "EquityIssuer",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_CommonStockListedCusip_EquityIssuer_CommonStockId",
                table: "CommonStockListedCusip",
                column: "CommonStockId",
                principalTable: "EquityIssuer",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_CommonStockTickerAlias_EquityIssuer_CommonStockId",
                table: "CommonStockTickerAlias",
                column: "CommonStockId",
                principalTable: "EquityIssuer",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("Returning identifier history to legacy owners could orphan preserved source evidence.");
        }
    }
}

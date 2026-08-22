using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class MakeCongressionalTradeAccountAware : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CongressionalTrade_CommonStockId_CongressMemberId_Transacti~",
                table: "CongressionalTrade");

            migrationBuilder.CreateIndex(
                name: "UX_CongressionalTrade_FilingIdentity",
                table: "CongressionalTrade",
                columns: new[] { "CommonStockId", "CongressMemberId", "TransactionDate", "TransactionType", "AssetName", "OwnerType", "AmountFrom", "AmountTo", "AssetType", "Subholding" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_CongressionalTrade_FilingIdentity",
                table: "CongressionalTrade");

            migrationBuilder.CreateIndex(
                name: "IX_CongressionalTrade_CommonStockId_CongressMemberId_Transacti~",
                table: "CongressionalTrade",
                columns: new[] { "CommonStockId", "CongressMemberId", "TransactionDate", "TransactionType", "AssetName", "OwnerType", "AmountFrom", "AmountTo" },
                unique: true);
        }
    }
}

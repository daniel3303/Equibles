using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class WidenCongressionalTradeUpsertKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CongressionalTrade_CommonStockId_CongressMemberId_Transacti~",
                table: "CongressionalTrade");

            migrationBuilder.AlterColumn<string>(
                name: "OwnerType",
                table: "CongressionalTrade",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_CongressionalTrade_CommonStockId_CongressMemberId_Transacti~",
                table: "CongressionalTrade",
                columns: new[] { "CommonStockId", "CongressMemberId", "TransactionDate", "TransactionType", "AssetName", "OwnerType", "AmountFrom", "AmountTo" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CongressionalTrade_CommonStockId_CongressMemberId_Transacti~",
                table: "CongressionalTrade");

            migrationBuilder.AlterColumn<string>(
                name: "OwnerType",
                table: "CongressionalTrade",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64,
                oldDefaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_CongressionalTrade_CommonStockId_CongressMemberId_Transacti~",
                table: "CongressionalTrade",
                columns: new[] { "CommonStockId", "CongressMemberId", "TransactionDate", "TransactionType", "AssetName" },
                unique: true);
        }
    }
}

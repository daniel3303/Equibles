using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class WidenInsiderTransactionUniqueIndexForMultiTxPerFiling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_InsiderTransaction_CommonStockId_InsiderOwnerId_Transaction~",
                table: "InsiderTransaction");

            migrationBuilder.CreateIndex(
                name: "IX_InsiderTransaction_CommonStockId_InsiderOwnerId_Transaction~",
                table: "InsiderTransaction",
                columns: new[] { "CommonStockId", "InsiderOwnerId", "TransactionDate", "TransactionCode", "SecurityTitle", "AccessionNumber", "OwnershipNature", "Shares", "PricePerShare", "SharesOwnedAfter" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_InsiderTransaction_CommonStockId_InsiderOwnerId_Transaction~",
                table: "InsiderTransaction");

            migrationBuilder.CreateIndex(
                name: "IX_InsiderTransaction_CommonStockId_InsiderOwnerId_Transaction~",
                table: "InsiderTransaction",
                columns: new[] { "CommonStockId", "InsiderOwnerId", "TransactionDate", "TransactionCode", "SecurityTitle", "AccessionNumber" },
                unique: true);
        }
    }
}

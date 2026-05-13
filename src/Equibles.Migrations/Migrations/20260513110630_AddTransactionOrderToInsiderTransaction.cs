using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddTransactionOrderToInsiderTransaction : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_InsiderTransaction_AccessionNumber",
                table: "InsiderTransaction");

            migrationBuilder.DropIndex(
                name: "IX_InsiderTransaction_CommonStockId_InsiderOwnerId_Transaction~",
                table: "InsiderTransaction");

            migrationBuilder.AddColumn<int>(
                name: "TransactionOrder",
                table: "InsiderTransaction",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_InsiderTransaction_AccessionNumber_TransactionOrder",
                table: "InsiderTransaction",
                columns: new[] { "AccessionNumber", "TransactionOrder" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_InsiderTransaction_AccessionNumber_TransactionOrder",
                table: "InsiderTransaction");

            migrationBuilder.DropColumn(
                name: "TransactionOrder",
                table: "InsiderTransaction");

            migrationBuilder.CreateIndex(
                name: "IX_InsiderTransaction_AccessionNumber",
                table: "InsiderTransaction",
                column: "AccessionNumber");

            migrationBuilder.CreateIndex(
                name: "IX_InsiderTransaction_CommonStockId_InsiderOwnerId_Transaction~",
                table: "InsiderTransaction",
                columns: new[] { "CommonStockId", "InsiderOwnerId", "TransactionDate", "TransactionCode", "SecurityTitle", "AccessionNumber", "OwnershipNature", "Shares", "PricePerShare", "SharesOwnedAfter" },
                unique: true);
        }
    }
}

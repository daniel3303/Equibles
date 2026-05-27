using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddInsiderTransactionPriceValidity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsPriceValid",
                table: "InsiderTransaction",
                type: "boolean",
                nullable: false,
                defaultValue: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_InsiderTransaction_IsPriceValid_TransactionDate",
                table: "InsiderTransaction",
                columns: new[] { "IsPriceValid", "TransactionDate" }
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_InsiderTransaction_IsPriceValid_TransactionDate",
                table: "InsiderTransaction"
            );

            migrationBuilder.DropColumn(name: "IsPriceValid", table: "InsiderTransaction");
        }
    }
}

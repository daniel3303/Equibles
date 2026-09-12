using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class RetargetIssuerDisclosures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Form144Filing_CommonStock_CommonStockId",
                table: "Form144Filing");

            migrationBuilder.DropForeignKey(
                name: "FK_GovernmentContract_CommonStock_CommonStockId",
                table: "GovernmentContract");

            migrationBuilder.DropForeignKey(
                name: "FK_InsiderTransaction_CommonStock_CommonStockId",
                table: "InsiderTransaction");

            migrationBuilder.AddForeignKey(
                name: "FK_FdaCatalyst_EquityIssuer_CommonStockId",
                table: "FdaCatalyst",
                column: "CommonStockId",
                principalTable: "EquityIssuer",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Form144Filing_EquityIssuer_CommonStockId",
                table: "Form144Filing",
                column: "CommonStockId",
                principalTable: "EquityIssuer",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_GovernmentContract_EquityIssuer_CommonStockId",
                table: "GovernmentContract",
                column: "CommonStockId",
                principalTable: "EquityIssuer",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_InsiderTransaction_EquityIssuer_CommonStockId",
                table: "InsiderTransaction",
                column: "CommonStockId",
                principalTable: "EquityIssuer",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("Issuer-owned disclosures cannot be retargeted to legacy stocks. Restore a verified backup instead.");
        }
    }
}

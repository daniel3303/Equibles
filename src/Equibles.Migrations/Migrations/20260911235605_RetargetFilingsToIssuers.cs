using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class RetargetFilingsToIssuers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Document_CommonStock_CommonStockId",
                table: "Document");

            migrationBuilder.DropForeignKey(
                name: "FK_FormDFiling_CommonStock_CommonStockId",
                table: "FormDFiling");

            migrationBuilder.DropForeignKey(
                name: "FK_NCenFiling_CommonStock_CommonStockId",
                table: "NCenFiling");

            migrationBuilder.DropForeignKey(
                name: "FK_NportFiling_CommonStock_CommonStockId",
                table: "NportFiling");

            migrationBuilder.AddForeignKey(
                name: "FK_Document_EquityIssuer_CommonStockId",
                table: "Document",
                column: "CommonStockId",
                principalTable: "EquityIssuer",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_FormDFiling_EquityIssuer_CommonStockId",
                table: "FormDFiling",
                column: "CommonStockId",
                principalTable: "EquityIssuer",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_NCenFiling_EquityIssuer_CommonStockId",
                table: "NCenFiling",
                column: "CommonStockId",
                principalTable: "EquityIssuer",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_NportFiling_EquityIssuer_CommonStockId",
                table: "NportFiling",
                column: "CommonStockId",
                principalTable: "EquityIssuer",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("Issuer-owned filings cannot be retargeted to legacy stocks. Restore a verified backup instead.");
        }
    }
}

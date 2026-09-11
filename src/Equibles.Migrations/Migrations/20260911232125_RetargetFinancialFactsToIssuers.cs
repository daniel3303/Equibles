using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class RetargetFinancialFactsToIssuers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_FinancialFact_CommonStock_CommonStockId",
                table: "FinancialFact");

            migrationBuilder.DropForeignKey(
                name: "FK_ReportedFinancialStatement_CommonStock_CommonStockId",
                table: "ReportedFinancialStatement");

            migrationBuilder.AddForeignKey(
                name: "FK_FinancialFact_EquityIssuer_CommonStockId",
                table: "FinancialFact",
                column: "CommonStockId",
                principalTable: "EquityIssuer",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ReportedFinancialStatement_EquityIssuer_CommonStockId",
                table: "ReportedFinancialStatement",
                column: "CommonStockId",
                principalTable: "EquityIssuer",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("Retain native issuer references when reverting application binaries.");
        }
    }
}

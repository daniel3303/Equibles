using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class RetargetIssuerIngestionCheckpoints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CompanyFilingSyncState_CommonStock_CommonStockId",
                table: "CompanyFilingSyncState");

            migrationBuilder.DropForeignKey(
                name: "FK_FinancialFactsSyncStatus_CommonStock_CommonStockId",
                table: "FinancialFactsSyncStatus");

            migrationBuilder.DropForeignKey(
                name: "FK_TranscriptCheckStatuses_CommonStock_CommonStockId",
                table: "TranscriptCheckStatuses");

            migrationBuilder.AddForeignKey(
                name: "FK_CompanyFilingSyncState_EquityIssuer_CommonStockId",
                table: "CompanyFilingSyncState",
                column: "CommonStockId",
                principalTable: "EquityIssuer",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_FinancialFactsSyncStatus_EquityIssuer_CommonStockId",
                table: "FinancialFactsSyncStatus",
                column: "CommonStockId",
                principalTable: "EquityIssuer",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_TranscriptCheckStatuses_EquityIssuer_CommonStockId",
                table: "TranscriptCheckStatuses",
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

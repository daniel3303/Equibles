using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class RetargetIssuerIdentityEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CommonStockTickerEvidence_CommonStock_CommonStockId",
                table: "CommonStockTickerEvidence");

            migrationBuilder.DropForeignKey(
                name: "FK_ListedSecurity_CommonStock_CommonStockId",
                table: "ListedSecurity");

            migrationBuilder.AddForeignKey(
                name: "FK_CommonStockTickerEvidence_EquityIssuer_CommonStockId",
                table: "CommonStockTickerEvidence",
                column: "CommonStockId",
                principalTable: "EquityIssuer",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ListedSecurity_EquityIssuer_CommonStockId",
                table: "ListedSecurity",
                column: "CommonStockId",
                principalTable: "EquityIssuer",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("Returning issuer evidence to legacy owners could orphan authoritative source records.");
        }
    }
}

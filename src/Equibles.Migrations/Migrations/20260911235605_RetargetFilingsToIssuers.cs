using Equibles.Migrations.Infrastructure;
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

            NativeIssuerForeignKeyRetarget20260912.Apply(migrationBuilder,
                "Document", "CommonStockId", "FK_Document_EquityIssuer_CommonStockId", "FK_Document_CommonStock_CommonStockId");

            NativeIssuerForeignKeyRetarget20260912.Apply(migrationBuilder,
                "FormDFiling", "CommonStockId", "FK_FormDFiling_EquityIssuer_CommonStockId", "FK_FormDFiling_CommonStock_CommonStockId");

            NativeIssuerForeignKeyRetarget20260912.Apply(migrationBuilder,
                "NCenFiling", "CommonStockId", "FK_NCenFiling_EquityIssuer_CommonStockId", "FK_NCenFiling_CommonStock_CommonStockId");

            NativeIssuerForeignKeyRetarget20260912.Apply(migrationBuilder,
                "NportFiling", "CommonStockId", "FK_NportFiling_EquityIssuer_CommonStockId", "FK_NportFiling_CommonStock_CommonStockId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("Issuer-owned filings cannot be retargeted to legacy stocks. Restore a verified backup instead.");
        }
    }
}

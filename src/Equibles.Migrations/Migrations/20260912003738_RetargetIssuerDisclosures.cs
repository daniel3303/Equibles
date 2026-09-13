using Equibles.Migrations.Infrastructure;
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

            NativeIssuerForeignKeyRetarget20260912.Apply(migrationBuilder,
                "FdaCatalyst", "CommonStockId", "FK_FdaCatalyst_EquityIssuer_CommonStockId", null);

            NativeIssuerForeignKeyRetarget20260912.Apply(migrationBuilder,
                "Form144Filing", "CommonStockId", "FK_Form144Filing_EquityIssuer_CommonStockId", "FK_Form144Filing_CommonStock_CommonStockId");

            NativeIssuerForeignKeyRetarget20260912.Apply(migrationBuilder,
                "GovernmentContract", "CommonStockId", "FK_GovernmentContract_EquityIssuer_CommonStockId", "FK_GovernmentContract_CommonStock_CommonStockId");

            NativeIssuerForeignKeyRetarget20260912.Apply(migrationBuilder,
                "InsiderTransaction", "CommonStockId", "FK_InsiderTransaction_EquityIssuer_CommonStockId", "FK_InsiderTransaction_CommonStock_CommonStockId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("Issuer-owned disclosures cannot be retargeted to legacy stocks. Restore a verified backup instead.");
        }
    }
}

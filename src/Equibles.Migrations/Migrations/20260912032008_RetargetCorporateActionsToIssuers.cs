using Equibles.Migrations.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class RetargetCorporateActionsToIssuers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {

            NativeIssuerForeignKeyRetarget20260912.Apply(migrationBuilder,
                "CashDividend", "CommonStockId", "FK_CashDividend_EquityIssuer_CommonStockId", "FK_CashDividend_CommonStock_CommonStockId");

            NativeIssuerForeignKeyRetarget20260912.Apply(migrationBuilder,
                "StockSplit", "CommonStockId", "FK_StockSplit_EquityIssuer_CommonStockId", "FK_StockSplit_CommonStock_CommonStockId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("Returning corporate actions to a legacy owner could orphan native issuer data.");
        }
    }
}

using Equibles.Migrations.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class RetargetDirectoryIdentityEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {

            NativeIssuerForeignKeyRetarget20260912.Apply(migrationBuilder,
                "CommonStockCusipAlias", "CommonStockId", "FK_CommonStockCusipAlias_EquityIssuer_CommonStockId", "FK_CommonStockCusipAlias_CommonStock_CommonStockId");

            NativeIssuerForeignKeyRetarget20260912.Apply(migrationBuilder,
                "CommonStockDelistedListing", "CommonStockId", "FK_CommonStockDelistedListing_EquityIssuer_CommonStockId", "FK_CommonStockDelistedListing_CommonStock_CommonStockId");

            NativeIssuerForeignKeyRetarget20260912.Apply(migrationBuilder,
                "CommonStockListedCusip", "CommonStockId", "FK_CommonStockListedCusip_EquityIssuer_CommonStockId", "FK_CommonStockListedCusip_CommonStock_CommonStockId");

            NativeIssuerForeignKeyRetarget20260912.Apply(migrationBuilder,
                "CommonStockTickerAlias", "CommonStockId", "FK_CommonStockTickerAlias_EquityIssuer_CommonStockId", "FK_CommonStockTickerAlias_CommonStock_CommonStockId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("Returning identifier history to legacy owners could orphan preserved source evidence.");
        }
    }
}

using Equibles.Migrations.Infrastructure;
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

            NativeIssuerForeignKeyRetarget20260912.Apply(migrationBuilder,
                "CommonStockTickerEvidence", "CommonStockId", "FK_CommonStockTickerEvidence_EquityIssuer_CommonStockId", "FK_CommonStockTickerEvidence_CommonStock_CommonStockId");

            NativeIssuerForeignKeyRetarget20260912.Apply(migrationBuilder,
                "ListedSecurity", "CommonStockId", "FK_ListedSecurity_EquityIssuer_CommonStockId", "FK_ListedSecurity_CommonStock_CommonStockId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("Returning issuer evidence to legacy owners could orphan authoritative source records.");
        }
    }
}

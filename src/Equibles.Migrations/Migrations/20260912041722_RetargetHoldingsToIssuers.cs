using Equibles.Migrations.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class RetargetHoldingsToIssuers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // These materialized histories previously had no owner constraint. Retain an
            // original owner GUID even if its former directory row has already disappeared.
            migrationBuilder.Sql("""
                INSERT INTO "EquityIssuer" ("Id", "SecondaryCiks")
                SELECT source."CommonStockId", ARRAY[]::text[]
                FROM (
                    SELECT "CommonStockId" FROM "StockQuarterlyActivity"
                    UNION SELECT "CommonStockId" FROM "StockQuarterlyActivityCombined"
                    UNION SELECT "CommonStockId" FROM "StockQuarterlyListingActivity"
                ) source
                WHERE NOT EXISTS (SELECT 1 FROM "EquityIssuer" issuer WHERE issuer."Id" = source."CommonStockId")
                ON CONFLICT ("Id") DO NOTHING;
                """);

            NativeIssuerForeignKeyRetarget20260912.Apply(migrationBuilder,
                "InstitutionalHolding", "CommonStockId", "FK_InstitutionalHolding_EquityIssuer_CommonStockId", "FK_InstitutionalHolding_CommonStock_CommonStockId");

            NativeIssuerForeignKeyRetarget20260912.Apply(migrationBuilder,
                "StockQuarterlyActivity", "CommonStockId", "FK_StockQuarterlyActivity_EquityIssuer_CommonStockId", null);

            NativeIssuerForeignKeyRetarget20260912.Apply(migrationBuilder,
                "StockQuarterlyActivityCombined", "CommonStockId", "FK_StockQuarterlyActivityCombined_EquityIssuer_CommonStockId", null);

            NativeIssuerForeignKeyRetarget20260912.Apply(migrationBuilder,
                "StockQuarterlyListingActivity", "CommonStockId", "FK_StockQuarterlyListingActivity_EquityIssuer_CommonStockId", null);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("Returning holdings to legacy owners could orphan positions and historical activity.");
        }
    }
}

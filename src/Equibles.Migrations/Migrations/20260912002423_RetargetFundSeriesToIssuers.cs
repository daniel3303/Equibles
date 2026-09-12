using Equibles.Migrations.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class RetargetFundSeriesToIssuers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            NativeIssuerForeignKeyRetarget20260912.Apply(migrationBuilder,
                "FundSeries", "CommonStockId", "FK_FundSeries_EquityIssuer_CommonStockId", null);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("Issuer-owned fund series cannot lose their ownership constraint. Restore a verified backup instead.");
        }
    }
}

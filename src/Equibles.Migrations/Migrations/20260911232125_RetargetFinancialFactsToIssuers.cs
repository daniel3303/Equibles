using Equibles.Migrations.Infrastructure;
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

            NativeIssuerForeignKeyRetarget20260912.Apply(migrationBuilder,
                "FinancialFact", "CommonStockId", "FK_FinancialFact_EquityIssuer_CommonStockId", "FK_FinancialFact_CommonStock_CommonStockId");

            NativeIssuerForeignKeyRetarget20260912.Apply(migrationBuilder,
                "ReportedFinancialStatement", "CommonStockId", "FK_ReportedFinancialStatement_EquityIssuer_CommonStockId", "FK_ReportedFinancialStatement_CommonStock_CommonStockId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("Retain native issuer references when reverting application binaries.");
        }
    }
}

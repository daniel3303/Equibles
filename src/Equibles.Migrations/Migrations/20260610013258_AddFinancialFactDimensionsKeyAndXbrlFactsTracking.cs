using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddFinancialFactDimensionsKeyAndXbrlFactsTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_FinancialFact_CommonStockId_FinancialConceptId_Unit_PeriodS~",
                table: "FinancialFact");

            migrationBuilder.AddColumn<string>(
                name: "DimensionsKey",
                table: "FinancialFact",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "XbrlFactsAttempts",
                table: "Document",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "XbrlFactsVersion",
                table: "Document",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_FinancialFact_CommonStockId_FinancialConceptId_Unit_PeriodS~",
                table: "FinancialFact",
                columns: new[] { "CommonStockId", "FinancialConceptId", "Unit", "PeriodStart", "PeriodEnd", "AccessionNumber", "DimensionsKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Document_XbrlStatus_XbrlFactsVersion",
                table: "Document",
                columns: new[] { "XbrlStatus", "XbrlFactsVersion" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_FinancialFact_CommonStockId_FinancialConceptId_Unit_PeriodS~",
                table: "FinancialFact");

            migrationBuilder.DropIndex(
                name: "IX_Document_XbrlStatus_XbrlFactsVersion",
                table: "Document");

            migrationBuilder.DropColumn(
                name: "DimensionsKey",
                table: "FinancialFact");

            migrationBuilder.DropColumn(
                name: "XbrlFactsAttempts",
                table: "Document");

            migrationBuilder.DropColumn(
                name: "XbrlFactsVersion",
                table: "Document");

            migrationBuilder.CreateIndex(
                name: "IX_FinancialFact_CommonStockId_FinancialConceptId_Unit_PeriodS~",
                table: "FinancialFact",
                columns: new[] { "CommonStockId", "FinancialConceptId", "Unit", "PeriodStart", "PeriodEnd", "AccessionNumber" },
                unique: true);
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class CoverStockQuarterExposure : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_InstitutionalHolding_StockQuarterExposure",
                table: "InstitutionalHolding",
                columns: new[] { "CommonStockId", "ReportDate" })
                .Annotation("Npgsql:CreatedConcurrently", true)
                .Annotation("Npgsql:IndexInclude", new[] { "InstitutionalHolderId", "Value", "Shares", "ListedTicker", "FilingType", "OptionType" });

            migrationBuilder.Sql(
                "DROP INDEX CONCURRENTLY IF EXISTS \"IX_InstitutionalHolding_CommonStockId_ReportDate\";",
                suppressTransaction: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_InstitutionalHolding_CommonStockId_ReportDate",
                table: "InstitutionalHolding",
                columns: new[] { "CommonStockId", "ReportDate" })
                .Annotation("Npgsql:CreatedConcurrently", true)
                .Annotation("Npgsql:IndexInclude", new[] { "InstitutionalHolderId", "Value", "Shares" });

            migrationBuilder.Sql(
                "DROP INDEX CONCURRENTLY IF EXISTS \"IX_InstitutionalHolding_StockQuarterExposure\";",
                suppressTransaction: true);
        }
    }
}

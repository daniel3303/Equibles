using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddStockQuarterlyActivityCombined : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StockQuarterlyActivityCombined",
                columns: table => new
                {
                    CommonStockId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReportDate = table.Column<DateOnly>(type: "date", nullable: false),
                    PreviousReportDate = table.Column<DateOnly>(type: "date", nullable: false),
                    CurrentShares = table.Column<long>(type: "bigint", nullable: false),
                    PreviousShares = table.Column<long>(type: "bigint", nullable: false),
                    CurrentValue = table.Column<long>(type: "bigint", nullable: false),
                    PreviousValue = table.Column<long>(type: "bigint", nullable: false),
                    CurrentFilerCount = table.Column<int>(type: "integer", nullable: false),
                    PreviousFilerCount = table.Column<int>(type: "integer", nullable: false),
                    NewFilerCount = table.Column<int>(type: "integer", nullable: false),
                    SoldOutFilerCount = table.Column<int>(type: "integer", nullable: false),
                    ComputedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockQuarterlyActivityCombined", x => new { x.CommonStockId, x.ReportDate });
                });

            migrationBuilder.CreateIndex(
                name: "IX_StockQuarterlyActivityCombined_ReportDate",
                table: "StockQuarterlyActivityCombined",
                column: "ReportDate");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StockQuarterlyActivityCombined");
        }
    }
}

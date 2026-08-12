using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddStockQuarterlyListingActivity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StockQuarterlyListingActivity",
                columns: table => new
                {
                    CommonStockId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReportDate = table.Column<DateOnly>(type: "date", nullable: false),
                    IsCombined = table.Column<bool>(type: "boolean", nullable: false),
                    PriceSeriesTicker = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CurrentShares = table.Column<long>(type: "bigint", nullable: false),
                    PreviousShares = table.Column<long>(type: "bigint", nullable: false),
                    ComputedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockQuarterlyListingActivity", x => new { x.CommonStockId, x.ReportDate, x.IsCombined, x.PriceSeriesTicker });
                });

            migrationBuilder.CreateIndex(
                name: "IX_StockQuarterlyListingActivity_ReportDate_IsCombined",
                table: "StockQuarterlyListingActivity",
                columns: new[] { "ReportDate", "IsCombined" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StockQuarterlyListingActivity");
        }
    }
}

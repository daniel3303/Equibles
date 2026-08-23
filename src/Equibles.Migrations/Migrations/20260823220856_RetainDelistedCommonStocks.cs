using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class RetainDelistedCommonStocks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CommonStock_Ticker",
                table: "CommonStock");

            migrationBuilder.AddColumn<bool>(
                name: "Active",
                table: "CommonStock",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "DelistedOn",
                table: "CommonStock",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "HistoricalPriceBackfillAttemptedAt",
                table: "CommonStock",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_CommonStock_Ticker",
                table: "CommonStock",
                column: "Ticker",
                unique: true,
                filter: "\"Active\"");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CommonStock_Ticker",
                table: "CommonStock");

            migrationBuilder.DropColumn(
                name: "Active",
                table: "CommonStock");

            migrationBuilder.DropColumn(
                name: "DelistedOn",
                table: "CommonStock");

            migrationBuilder.DropColumn(
                name: "HistoricalPriceBackfillAttemptedAt",
                table: "CommonStock");

            migrationBuilder.CreateIndex(
                name: "IX_CommonStock_Ticker",
                table: "CommonStock",
                column: "Ticker",
                unique: true);
        }
    }
}

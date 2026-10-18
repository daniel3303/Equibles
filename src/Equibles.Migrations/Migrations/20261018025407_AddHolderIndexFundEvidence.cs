using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddHolderIndexFundEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "AdvisedSeriesAsOf",
                table: "HolderQuarterlyMetrics",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "AdvisedSeriesNetAssets",
                table: "HolderQuarterlyMetrics",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "IndexSeriesNetAssets",
                table: "HolderQuarterlyMetrics",
                type: "numeric",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AdvisedSeriesAsOf",
                table: "HolderQuarterlyMetrics");

            migrationBuilder.DropColumn(
                name: "AdvisedSeriesNetAssets",
                table: "HolderQuarterlyMetrics");

            migrationBuilder.DropColumn(
                name: "IndexSeriesNetAssets",
                table: "HolderQuarterlyMetrics");
        }
    }
}

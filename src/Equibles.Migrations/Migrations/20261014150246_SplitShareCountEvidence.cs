using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class SplitShareCountEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ShareCountEvidence",
                table: "StockSplit",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "ShareCountEvidenceAsOf",
                table: "StockSplit",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ShareCountEvidenceTime",
                table: "StockSplit",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ShareCountEvidence",
                table: "StockSplit");

            migrationBuilder.DropColumn(
                name: "ShareCountEvidenceAsOf",
                table: "StockSplit");

            migrationBuilder.DropColumn(
                name: "ShareCountEvidenceTime",
                table: "StockSplit");
        }
    }
}

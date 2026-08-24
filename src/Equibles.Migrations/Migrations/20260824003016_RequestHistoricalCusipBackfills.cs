using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class RequestHistoricalCusipBackfills : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "HistoricalCusipBackfillAmbiguous",
                table: "CommonStock",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateOnly>(
                name: "HistoricalCusipBackfillCandidateOn",
                table: "CommonStock",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<List<string>>(
                name: "HistoricalCusipBackfillCandidates",
                table: "CommonStock",
                type: "text[]",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "HistoricalCusipBackfillRequestedAt",
                table: "CommonStock",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "HistoricalCusipBackfillSweepStartedAt",
                table: "CommonStock",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HistoricalCusipBackfillAmbiguous",
                table: "CommonStock");

            migrationBuilder.DropColumn(
                name: "HistoricalCusipBackfillCandidateOn",
                table: "CommonStock");

            migrationBuilder.DropColumn(
                name: "HistoricalCusipBackfillCandidates",
                table: "CommonStock");

            migrationBuilder.DropColumn(
                name: "HistoricalCusipBackfillRequestedAt",
                table: "CommonStock");

            migrationBuilder.DropColumn(
                name: "HistoricalCusipBackfillSweepStartedAt",
                table: "CommonStock");
        }
    }
}

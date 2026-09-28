using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddDurableImportRecovery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ImportAttemptId",
                table: "FinancialFactsSyncStatus",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ImportAttempts",
                table: "FinancialFactsSyncStatus",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "NextAttemptAt",
                table: "FinancialFactsSyncStatus",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "HoldingsCusipRescan",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EquityIssuerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Ticker = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    PreviousCusip = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Cusip = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    RequestedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ScannedThrough = table.Column<DateOnly>(type: "date", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HoldingsCusipRescan", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HoldingsCusipRescan_CompletedAt_RequestedAt",
                table: "HoldingsCusipRescan",
                columns: new[] { "CompletedAt", "RequestedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HoldingsCusipRescan");

            migrationBuilder.DropColumn(
                name: "ImportAttemptId",
                table: "FinancialFactsSyncStatus");

            migrationBuilder.DropColumn(
                name: "ImportAttempts",
                table: "FinancialFactsSyncStatus");

            migrationBuilder.DropColumn(
                name: "NextAttemptAt",
                table: "FinancialFactsSyncStatus");
        }
    }
}

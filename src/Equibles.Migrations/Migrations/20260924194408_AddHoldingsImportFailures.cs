using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddHoldingsImportFailures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "HoldingsImportFailure",
                columns: table => new
                {
                    AccessionNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Cik = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    FilingDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ReportDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Reason = table.Column<int>(type: "integer", nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    FirstFailedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastAttemptAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    NextAttemptAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ResolvedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HoldingsImportFailure", x => x.AccessionNumber);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HoldingsImportFailure_ResolvedAt_NextAttemptAt",
                table: "HoldingsImportFailure",
                columns: new[] { "ResolvedAt", "NextAttemptAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HoldingsImportFailure");
        }
    }
}

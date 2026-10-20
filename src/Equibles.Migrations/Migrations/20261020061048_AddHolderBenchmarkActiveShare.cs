using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddHolderBenchmarkActiveShare : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "ClosestActiveShare",
                table: "HolderQuarterlyMetrics",
                type: "numeric(12,6)",
                precision: 12,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ClosestBenchmark",
                table: "HolderQuarterlyMetrics",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "ClosestBenchmarkAsOf",
                table: "HolderQuarterlyMetrics",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ClosestBenchmarkName",
                table: "HolderQuarterlyMetrics",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ComparedHolderId",
                table: "HolderQuarterlyMetrics",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "SectorActiveShare",
                table: "HolderQuarterlyMetrics",
                type: "numeric(12,6)",
                precision: 12,
                scale: 6,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "HolderQuarterlyBenchmarkActiveShare",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InstitutionalHolderId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReportDate = table.Column<DateOnly>(type: "date", nullable: false),
                    BenchmarkKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    BenchmarkAsOf = table.Column<DateOnly>(type: "date", nullable: false),
                    ActiveShare = table.Column<decimal>(type: "numeric(12,6)", precision: 12, scale: 6, nullable: false),
                    Overlap = table.Column<decimal>(type: "numeric(12,6)", precision: 12, scale: 6, nullable: false),
                    Coverage = table.Column<decimal>(type: "numeric(12,6)", precision: 12, scale: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HolderQuarterlyBenchmarkActiveShare", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HolderQuarterlyBenchmarkActiveShare_InstitutionalHolder_Ins~",
                        column: x => x.InstitutionalHolderId,
                        principalTable: "InstitutionalHolder",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HolderQuarterlyMetrics_ComparedHolderId",
                table: "HolderQuarterlyMetrics",
                column: "ComparedHolderId");

            migrationBuilder.CreateIndex(
                name: "IX_HolderQuarterlyBenchmarkActiveShare_InstitutionalHolderId_R~",
                table: "HolderQuarterlyBenchmarkActiveShare",
                columns: new[] { "InstitutionalHolderId", "ReportDate", "BenchmarkKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HolderQuarterlyBenchmarkActiveShare_ReportDate_BenchmarkKey",
                table: "HolderQuarterlyBenchmarkActiveShare",
                columns: new[] { "ReportDate", "BenchmarkKey" });

            migrationBuilder.AddForeignKey(
                name: "FK_HolderQuarterlyMetrics_InstitutionalHolder_ComparedHolderId",
                table: "HolderQuarterlyMetrics",
                column: "ComparedHolderId",
                principalTable: "InstitutionalHolder",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_HolderQuarterlyMetrics_InstitutionalHolder_ComparedHolderId",
                table: "HolderQuarterlyMetrics");

            migrationBuilder.DropTable(
                name: "HolderQuarterlyBenchmarkActiveShare");

            migrationBuilder.DropIndex(
                name: "IX_HolderQuarterlyMetrics_ComparedHolderId",
                table: "HolderQuarterlyMetrics");

            migrationBuilder.DropColumn(
                name: "ClosestActiveShare",
                table: "HolderQuarterlyMetrics");

            migrationBuilder.DropColumn(
                name: "ClosestBenchmark",
                table: "HolderQuarterlyMetrics");

            migrationBuilder.DropColumn(
                name: "ClosestBenchmarkAsOf",
                table: "HolderQuarterlyMetrics");

            migrationBuilder.DropColumn(
                name: "ClosestBenchmarkName",
                table: "HolderQuarterlyMetrics");

            migrationBuilder.DropColumn(
                name: "ComparedHolderId",
                table: "HolderQuarterlyMetrics");

            migrationBuilder.DropColumn(
                name: "SectorActiveShare",
                table: "HolderQuarterlyMetrics");
        }
    }
}

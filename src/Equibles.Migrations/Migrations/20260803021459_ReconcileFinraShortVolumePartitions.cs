using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class ReconcileFinraShortVolumePartitions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<decimal>(
                name: "TotalVolume",
                table: "DailyShortVolume",
                type: "numeric(28,6)",
                precision: 28,
                scale: 6,
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AlterColumn<decimal>(
                name: "ShortVolume",
                table: "DailyShortVolume",
                type: "numeric(28,6)",
                precision: 28,
                scale: 6,
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AlterColumn<decimal>(
                name: "ShortExemptVolume",
                table: "DailyShortVolume",
                type: "numeric(28,6)",
                precision: 28,
                scale: 6,
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.CreateTable(
                name: "FinraImportPartition",
                columns: table => new
                {
                    Dataset = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PartitionDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ScopeKey = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    ImportedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinraImportPartition", x => new { x.Dataset, x.PartitionDate, x.ScopeKey });
                });

            migrationBuilder.CreateIndex(
                name: "IX_FinraImportPartition_Dataset_ScopeKey_PartitionDate",
                table: "FinraImportPartition",
                columns: new[] { "Dataset", "ScopeKey", "PartitionDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FinraImportPartition");

            migrationBuilder.AlterColumn<long>(
                name: "TotalVolume",
                table: "DailyShortVolume",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(28,6)",
                oldPrecision: 28,
                oldScale: 6);

            migrationBuilder.AlterColumn<long>(
                name: "ShortVolume",
                table: "DailyShortVolume",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(28,6)",
                oldPrecision: 28,
                oldScale: 6);

            migrationBuilder.AlterColumn<long>(
                name: "ShortExemptVolume",
                table: "DailyShortVolume",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(28,6)",
                oldPrecision: 28,
                oldScale: 6);
        }
    }
}

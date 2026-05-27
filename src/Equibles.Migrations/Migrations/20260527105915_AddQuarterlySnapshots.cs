using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddQuarterlySnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AumQuarterlySnapshot",
                columns: table => new
                {
                    ReportDate = table.Column<DateOnly>(type: "date", nullable: false),
                    TotalValue = table.Column<long>(type: "bigint", nullable: false),
                    FilerCount = table.Column<int>(type: "integer", nullable: false),
                    PositionCount = table.Column<int>(type: "integer", nullable: false),
                    StockCount = table.Column<int>(type: "integer", nullable: false),
                    FilingCount = table.Column<int>(type: "integer", nullable: false),
                    ComputedAt = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AumQuarterlySnapshot", x => x.ReportDate);
                }
            );

            migrationBuilder.CreateTable(
                name: "SectorQuarterlySnapshot",
                columns: table => new
                {
                    ReportDate = table.Column<DateOnly>(type: "date", nullable: false),
                    SectorId = table.Column<Guid>(type: "uuid", nullable: false),
                    SectorName = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: true
                    ),
                    TotalValue = table.Column<long>(type: "bigint", nullable: false),
                    ComputedAt = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey(
                        "PK_SectorQuarterlySnapshot",
                        x => new { x.ReportDate, x.SectorId }
                    );
                }
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "AumQuarterlySnapshot");

            migrationBuilder.DropTable(name: "SectorQuarterlySnapshot");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddHolderQuarterlySnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "HolderQuarterlySnapshot",
                columns: table => new
                {
                    InstitutionalHolderId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReportDate = table.Column<DateOnly>(type: "date", nullable: false),
                    FilingDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Aum = table.Column<long>(type: "bigint", nullable: false),
                    PositionCount = table.Column<int>(type: "integer", nullable: false),
                    StockCount = table.Column<int>(type: "integer", nullable: false),
                    ComputedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HolderQuarterlySnapshot", x => new { x.InstitutionalHolderId, x.ReportDate });
                });

            migrationBuilder.CreateIndex(
                name: "IX_HolderQuarterlySnapshot_ReportDate",
                table: "HolderQuarterlySnapshot",
                column: "ReportDate");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HolderQuarterlySnapshot");
        }
    }
}

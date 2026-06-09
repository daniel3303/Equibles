using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddOffExchangeVolume : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OffExchangeVolume",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CommonStockId = table.Column<Guid>(type: "uuid", nullable: false),
                    WeekStartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    AtsVolume = table.Column<long>(type: "bigint", nullable: false),
                    AtsTradeCount = table.Column<long>(type: "bigint", nullable: false),
                    NonAtsOtcVolume = table.Column<long>(type: "bigint", nullable: false),
                    NonAtsOtcTradeCount = table.Column<long>(type: "bigint", nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OffExchangeVolume", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OffExchangeVolume_CommonStock_CommonStockId",
                        column: x => x.CommonStockId,
                        principalTable: "CommonStock",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OffExchangeVolume_CommonStockId_WeekStartDate",
                table: "OffExchangeVolume",
                columns: new[] { "CommonStockId", "WeekStartDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OffExchangeVolume_WeekStartDate",
                table: "OffExchangeVolume",
                column: "WeekStartDate");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OffExchangeVolume");
        }
    }
}

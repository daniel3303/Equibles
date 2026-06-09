using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddIrNewsEventsAndEarningsCalendar : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EarningsCalendarEntry",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CommonStockId = table.Column<Guid>(type: "uuid", nullable: false),
                    FiscalYear = table.Column<int>(type: "integer", nullable: false),
                    FiscalQuarter = table.Column<int>(type: "integer", nullable: false),
                    ScheduledDate = table.Column<DateOnly>(type: "date", nullable: false),
                    IsConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    Url = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    Source = table.Column<int>(type: "integer", nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EarningsCalendarEntry", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EarningsCalendarEntry_CommonStock_CommonStockId",
                        column: x => x.CommonStockId,
                        principalTable: "CommonStock",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "IrEvent",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CommonStockId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    StartDateTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Url = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    Source = table.Column<int>(type: "integer", nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IrEvent", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IrEvent_CommonStock_CommonStockId",
                        column: x => x.CommonStockId,
                        principalTable: "CommonStock",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "IrNewsItem",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CommonStockId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    Url = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    PublishedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Summary = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    Source = table.Column<int>(type: "integer", nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IrNewsItem", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IrNewsItem_CommonStock_CommonStockId",
                        column: x => x.CommonStockId,
                        principalTable: "CommonStock",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EarningsCalendarEntry_CommonStockId_FiscalYear_FiscalQuarter",
                table: "EarningsCalendarEntry",
                columns: new[] { "CommonStockId", "FiscalYear", "FiscalQuarter" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EarningsCalendarEntry_ScheduledDate",
                table: "EarningsCalendarEntry",
                column: "ScheduledDate");

            migrationBuilder.CreateIndex(
                name: "IX_IrEvent_CommonStockId_StartDateTime",
                table: "IrEvent",
                columns: new[] { "CommonStockId", "StartDateTime" });

            migrationBuilder.CreateIndex(
                name: "IX_IrEvent_CommonStockId_StartDateTime_Title",
                table: "IrEvent",
                columns: new[] { "CommonStockId", "StartDateTime", "Title" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IrNewsItem_CommonStockId_PublishedAt",
                table: "IrNewsItem",
                columns: new[] { "CommonStockId", "PublishedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_IrNewsItem_CommonStockId_Url",
                table: "IrNewsItem",
                columns: new[] { "CommonStockId", "Url" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EarningsCalendarEntry");

            migrationBuilder.DropTable(
                name: "IrEvent");

            migrationBuilder.DropTable(
                name: "IrNewsItem");
        }
    }
}

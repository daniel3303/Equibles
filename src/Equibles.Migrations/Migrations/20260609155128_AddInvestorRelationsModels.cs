using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddInvestorRelationsModels : Migration
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
                    ScheduledDate = table.Column<DateOnly>(type: "date", nullable: false),
                    FiscalPeriod = table.Column<int>(type: "integer", nullable: false),
                    FiscalYear = table.Column<int>(type: "integer", nullable: true),
                    TimeOfDay = table.Column<int>(type: "integer", nullable: false),
                    IsConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    Url = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
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
                    Title = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    EventType = table.Column<int>(type: "integer", nullable: false),
                    ScheduledDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Url = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    Location = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
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
                    Title = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Url = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Summary = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    PublishedDate = table.Column<DateOnly>(type: "date", nullable: false),
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
                name: "IX_EarningsCalendarEntry_CommonStockId_ScheduledDate",
                table: "EarningsCalendarEntry",
                columns: new[] { "CommonStockId", "ScheduledDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EarningsCalendarEntry_ScheduledDate",
                table: "EarningsCalendarEntry",
                column: "ScheduledDate");

            migrationBuilder.CreateIndex(
                name: "IX_IrEvent_CommonStockId_Title_ScheduledDate",
                table: "IrEvent",
                columns: new[] { "CommonStockId", "Title", "ScheduledDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IrEvent_ScheduledDate",
                table: "IrEvent",
                column: "ScheduledDate");

            migrationBuilder.CreateIndex(
                name: "IX_IrNewsItem_CommonStockId_Url",
                table: "IrNewsItem",
                columns: new[] { "CommonStockId", "Url" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IrNewsItem_PublishedDate",
                table: "IrNewsItem",
                column: "PublishedDate");
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

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddFundSeries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FundSeries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IdentityKey = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Slug = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    CommonStockId = table.Column<Guid>(type: "uuid", nullable: true),
                    RegistrantCik = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    SeriesId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SeriesName = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    RegistrantName = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    Ticker = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    LatestReportPeriodDate = table.Column<DateOnly>(type: "date", nullable: false),
                    LatestFilingDate = table.Column<DateOnly>(type: "date", nullable: false),
                    NetAssets = table.Column<decimal>(type: "numeric", nullable: false),
                    TotalAssets = table.Column<decimal>(type: "numeric", nullable: false),
                    PositionCount = table.Column<int>(type: "integer", nullable: false),
                    FundType = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    ComputedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FundSeries", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FundSeries_CommonStockId",
                table: "FundSeries",
                column: "CommonStockId");

            migrationBuilder.CreateIndex(
                name: "IX_FundSeries_IdentityKey",
                table: "FundSeries",
                column: "IdentityKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FundSeries_RegistrantCik_SeriesId",
                table: "FundSeries",
                columns: new[] { "RegistrantCik", "SeriesId" });

            migrationBuilder.CreateIndex(
                name: "IX_FundSeries_Slug",
                table: "FundSeries",
                column: "Slug",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FundSeries");
        }
    }
}

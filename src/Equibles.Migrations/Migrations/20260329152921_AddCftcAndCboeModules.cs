using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddCftcAndCboeModules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CboePutCallRatio",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RatioType = table.Column<int>(type: "integer", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    CallVolume = table.Column<long>(type: "bigint", nullable: true),
                    PutVolume = table.Column<long>(type: "bigint", nullable: true),
                    TotalVolume = table.Column<long>(type: "bigint", nullable: true),
                    PutCallRatio = table.Column<decimal>(type: "numeric", nullable: true),
                    CreationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CboePutCallRatio", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CboeVixDaily",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Open = table.Column<decimal>(type: "numeric", nullable: false),
                    High = table.Column<decimal>(type: "numeric", nullable: false),
                    Low = table.Column<decimal>(type: "numeric", nullable: false),
                    Close = table.Column<decimal>(type: "numeric", nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CboeVixDaily", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CftcContract",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MarketCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    MarketName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Category = table.Column<int>(type: "integer", nullable: false),
                    LatestReportDate = table.Column<DateOnly>(type: "date", nullable: true),
                    LastUpdated = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CftcContract", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CftcPositionReport",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CftcContractId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReportDate = table.Column<DateOnly>(type: "date", nullable: false),
                    OpenInterest = table.Column<long>(type: "bigint", nullable: false),
                    NonCommLong = table.Column<long>(type: "bigint", nullable: false),
                    NonCommShort = table.Column<long>(type: "bigint", nullable: false),
                    NonCommSpreads = table.Column<long>(type: "bigint", nullable: false),
                    CommLong = table.Column<long>(type: "bigint", nullable: false),
                    CommShort = table.Column<long>(type: "bigint", nullable: false),
                    TotalRptLong = table.Column<long>(type: "bigint", nullable: false),
                    TotalRptShort = table.Column<long>(type: "bigint", nullable: false),
                    NonRptLong = table.Column<long>(type: "bigint", nullable: false),
                    NonRptShort = table.Column<long>(type: "bigint", nullable: false),
                    ChangeOpenInterest = table.Column<long>(type: "bigint", nullable: true),
                    ChangeNonCommLong = table.Column<long>(type: "bigint", nullable: true),
                    ChangeNonCommShort = table.Column<long>(type: "bigint", nullable: true),
                    ChangeCommLong = table.Column<long>(type: "bigint", nullable: true),
                    ChangeCommShort = table.Column<long>(type: "bigint", nullable: true),
                    PctNonCommLong = table.Column<decimal>(type: "numeric", nullable: true),
                    PctNonCommShort = table.Column<decimal>(type: "numeric", nullable: true),
                    PctCommLong = table.Column<decimal>(type: "numeric", nullable: true),
                    PctCommShort = table.Column<decimal>(type: "numeric", nullable: true),
                    TradersTotal = table.Column<int>(type: "integer", nullable: true),
                    TradersNonCommLong = table.Column<int>(type: "integer", nullable: true),
                    TradersNonCommShort = table.Column<int>(type: "integer", nullable: true),
                    TradersCommLong = table.Column<int>(type: "integer", nullable: true),
                    TradersCommShort = table.Column<int>(type: "integer", nullable: true),
                    CreationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CftcPositionReport", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CftcPositionReport_CftcContract_CftcContractId",
                        column: x => x.CftcContractId,
                        principalTable: "CftcContract",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CboePutCallRatio_Date",
                table: "CboePutCallRatio",
                column: "Date");

            migrationBuilder.CreateIndex(
                name: "IX_CboePutCallRatio_RatioType_Date",
                table: "CboePutCallRatio",
                columns: new[] { "RatioType", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CboeVixDaily_Date",
                table: "CboeVixDaily",
                column: "Date",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CftcContract_Category",
                table: "CftcContract",
                column: "Category");

            migrationBuilder.CreateIndex(
                name: "IX_CftcContract_MarketCode",
                table: "CftcContract",
                column: "MarketCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CftcPositionReport_CftcContractId_ReportDate",
                table: "CftcPositionReport",
                columns: new[] { "CftcContractId", "ReportDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CftcPositionReport_ReportDate",
                table: "CftcPositionReport",
                column: "ReportDate");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CboePutCallRatio");

            migrationBuilder.DropTable(
                name: "CboeVixDaily");

            migrationBuilder.DropTable(
                name: "CftcPositionReport");

            migrationBuilder.DropTable(
                name: "CftcContract");
        }
    }
}

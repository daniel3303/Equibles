using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddFundOwnership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FundOwnershipQuarterState",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReportDate = table.Column<DateOnly>(type: "date", nullable: false),
                    InputFingerprint = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ComputedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CalculationVersion = table.Column<int>(type: "integer", nullable: false),
                    SeriesCount = table.Column<int>(type: "integer", nullable: false),
                    UnclassifiedSeriesCount = table.Column<int>(type: "integer", nullable: false),
                    AttemptedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FailureCount = table.Column<int>(type: "integer", nullable: false),
                    LastError = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FundOwnershipQuarterState", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "HolderStockAdvisedFundShares",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InstitutionalHolderId = table.Column<Guid>(type: "uuid", nullable: false),
                    EquityIssuerId = table.Column<Guid>(type: "uuid", nullable: false),
                    ListedTicker = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    ReportDate = table.Column<DateOnly>(type: "date", nullable: false),
                    HolderShares = table.Column<long>(type: "bigint", nullable: false),
                    FundShares = table.Column<long>(type: "bigint", nullable: false),
                    IndexFundShares = table.Column<long>(type: "bigint", nullable: false),
                    SeriesCount = table.Column<int>(type: "integer", nullable: false),
                    IndexSeriesCount = table.Column<int>(type: "integer", nullable: false),
                    IsCapped = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HolderStockAdvisedFundShares", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HolderStockAdvisedFundShares_EquityIssuer_EquityIssuerId",
                        column: x => x.EquityIssuerId,
                        principalTable: "EquityIssuer",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_HolderStockAdvisedFundShares_InstitutionalHolder_Institutio~",
                        column: x => x.InstitutionalHolderId,
                        principalTable: "InstitutionalHolder",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StockQuarterlyFundOwnership",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EquityIssuerId = table.Column<Guid>(type: "uuid", nullable: false),
                    ListedTicker = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    ReportDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Bucket = table.Column<int>(type: "integer", nullable: false),
                    Shares = table.Column<long>(type: "bigint", nullable: false),
                    SplitUnresolvedShares = table.Column<long>(type: "bigint", nullable: false),
                    Value = table.Column<decimal>(type: "numeric(20,2)", precision: 20, scale: 2, nullable: false),
                    SeriesCount = table.Column<int>(type: "integer", nullable: false),
                    OldestReportPeriod = table.Column<DateOnly>(type: "date", nullable: false),
                    NewestReportPeriod = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockQuarterlyFundOwnership", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StockQuarterlyFundOwnership_EquityIssuer_EquityIssuerId",
                        column: x => x.EquityIssuerId,
                        principalTable: "EquityIssuer",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FundOwnershipQuarterState_ReportDate",
                table: "FundOwnershipQuarterState",
                column: "ReportDate",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HolderStockAdvisedFundShares_EquityIssuerId_ReportDate",
                table: "HolderStockAdvisedFundShares",
                columns: new[] { "EquityIssuerId", "ReportDate" });

            migrationBuilder.CreateIndex(
                name: "IX_HolderStockAdvisedFundShares_InstitutionalHolderId_EquityIs~",
                table: "HolderStockAdvisedFundShares",
                columns: new[] { "InstitutionalHolderId", "EquityIssuerId", "ListedTicker", "ReportDate" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_HolderStockAdvisedFundShares_ReportDate",
                table: "HolderStockAdvisedFundShares",
                column: "ReportDate");

            migrationBuilder.CreateIndex(
                name: "IX_StockQuarterlyFundOwnership_EquityIssuerId_ListedTicker_Rep~",
                table: "StockQuarterlyFundOwnership",
                columns: new[] { "EquityIssuerId", "ListedTicker", "ReportDate", "Bucket" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_StockQuarterlyFundOwnership_ReportDate",
                table: "StockQuarterlyFundOwnership",
                column: "ReportDate");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FundOwnershipQuarterState");

            migrationBuilder.DropTable(
                name: "HolderStockAdvisedFundShares");

            migrationBuilder.DropTable(
                name: "StockQuarterlyFundOwnership");
        }
    }
}

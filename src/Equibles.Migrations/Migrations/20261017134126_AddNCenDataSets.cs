using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddNCenDataSets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "NCenDataSetImport",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Year = table.Column<int>(type: "integer", nullable: false),
                    Quarter = table.Column<int>(type: "integer", nullable: false),
                    FileName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ParserVersion = table.Column<int>(type: "integer", nullable: false),
                    ReportCount = table.Column<int>(type: "integer", nullable: false),
                    ImportedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NCenDataSetImport", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "NCenRegistrantReport",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccessionNumber = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    RegistrantCik = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    RegistrantName = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    InvestmentCompanyFileNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    RegistrantLei = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    InvestmentCompanyType = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    FilingDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ReportEndingPeriod = table.Column<DateOnly>(type: "date", nullable: false),
                    IsAmendment = table.Column<bool>(type: "boolean", nullable: false),
                    IsFamilyInvestmentCompany = table.Column<bool>(type: "boolean", nullable: true),
                    FamilyInvestmentCompanyName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    TotalSeries = table.Column<int>(type: "integer", nullable: true),
                    DataSetYear = table.Column<int>(type: "integer", nullable: false),
                    DataSetQuarter = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NCenRegistrantReport", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "NCenFundReport",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RegistrantReportId = table.Column<Guid>(type: "uuid", nullable: false),
                    FundId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    SeriesId = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    FundName = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    FundLei = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    RegistrantCik = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    AccessionNumber = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    FilingDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ReportEndingPeriod = table.Column<DateOnly>(type: "date", nullable: false),
                    FundTypes = table.Column<int>(type: "integer", nullable: false),
                    IsIndexAffiliated = table.Column<bool>(type: "boolean", nullable: true),
                    IsIndexExclusive = table.Column<bool>(type: "boolean", nullable: true),
                    MonthlyAverageNetAssets = table.Column<decimal>(type: "numeric", nullable: true),
                    DailyAverageNetAssets = table.Column<decimal>(type: "numeric", nullable: true),
                    IsSuperseded = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NCenFundReport", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NCenFundReport_NCenRegistrantReport_RegistrantReportId",
                        column: x => x.RegistrantReportId,
                        principalTable: "NCenRegistrantReport",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "NCenFundAdviser",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FundReportId = table.Column<Guid>(type: "uuid", nullable: false),
                    Role = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    FileNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Crd = table.Column<int>(type: "integer", nullable: true),
                    Lei = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    State = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    Country = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    IsAffiliated = table.Column<bool>(type: "boolean", nullable: true),
                    IsHiredDuringPeriod = table.Column<bool>(type: "boolean", nullable: true),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: true),
                    TerminationDate = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NCenFundAdviser", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NCenFundAdviser_NCenFundReport_FundReportId",
                        column: x => x.FundReportId,
                        principalTable: "NCenFundReport",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "NCenFundExchangeListing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FundReportId = table.Column<Guid>(type: "uuid", nullable: false),
                    Exchange = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    Ticker = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NCenFundExchangeListing", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NCenFundExchangeListing_NCenFundReport_FundReportId",
                        column: x => x.FundReportId,
                        principalTable: "NCenFundReport",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "NCenFundShareClass",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FundReportId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClassId = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    ClassName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Ticker = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NCenFundShareClass", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NCenFundShareClass_NCenFundReport_FundReportId",
                        column: x => x.FundReportId,
                        principalTable: "NCenFundReport",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NCenDataSetImport_Year_Quarter",
                table: "NCenDataSetImport",
                columns: new[] { "Year", "Quarter" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NCenFundAdviser_Crd",
                table: "NCenFundAdviser",
                column: "Crd");

            migrationBuilder.CreateIndex(
                name: "IX_NCenFundAdviser_FundReportId",
                table: "NCenFundAdviser",
                column: "FundReportId");

            migrationBuilder.CreateIndex(
                name: "IX_NCenFundExchangeListing_FundReportId",
                table: "NCenFundExchangeListing",
                column: "FundReportId");

            migrationBuilder.CreateIndex(
                name: "IX_NCenFundExchangeListing_Ticker",
                table: "NCenFundExchangeListing",
                column: "Ticker");

            migrationBuilder.CreateIndex(
                name: "IX_NCenFundReport_FundId",
                table: "NCenFundReport",
                column: "FundId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NCenFundReport_RegistrantCik_ReportEndingPeriod_SeriesId",
                table: "NCenFundReport",
                columns: new[] { "RegistrantCik", "ReportEndingPeriod", "SeriesId" });

            migrationBuilder.CreateIndex(
                name: "IX_NCenFundReport_RegistrantReportId",
                table: "NCenFundReport",
                column: "RegistrantReportId");

            migrationBuilder.CreateIndex(
                name: "IX_NCenFundReport_SeriesId_ReportEndingPeriod",
                table: "NCenFundReport",
                columns: new[] { "SeriesId", "ReportEndingPeriod" });

            migrationBuilder.CreateIndex(
                name: "IX_NCenFundShareClass_FundReportId",
                table: "NCenFundShareClass",
                column: "FundReportId");

            migrationBuilder.CreateIndex(
                name: "IX_NCenFundShareClass_Ticker",
                table: "NCenFundShareClass",
                column: "Ticker");

            migrationBuilder.CreateIndex(
                name: "IX_NCenRegistrantReport_AccessionNumber",
                table: "NCenRegistrantReport",
                column: "AccessionNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NCenRegistrantReport_DataSetYear_DataSetQuarter",
                table: "NCenRegistrantReport",
                columns: new[] { "DataSetYear", "DataSetQuarter" });

            migrationBuilder.CreateIndex(
                name: "IX_NCenRegistrantReport_RegistrantCik_ReportEndingPeriod",
                table: "NCenRegistrantReport",
                columns: new[] { "RegistrantCik", "ReportEndingPeriod" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NCenDataSetImport");

            migrationBuilder.DropTable(
                name: "NCenFundAdviser");

            migrationBuilder.DropTable(
                name: "NCenFundExchangeListing");

            migrationBuilder.DropTable(
                name: "NCenFundShareClass");

            migrationBuilder.DropTable(
                name: "NCenFundReport");

            migrationBuilder.DropTable(
                name: "NCenRegistrantReport");
        }
    }
}

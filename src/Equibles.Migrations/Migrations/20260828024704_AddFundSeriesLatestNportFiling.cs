using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddFundSeriesLatestNportFiling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "LatestNportFilingId",
                table: "FundSeries",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.Sql(
                """
                UPDATE "FundSeries" AS series
                SET "LatestNportFilingId" = (
                    SELECT filing."Id"
                    FROM "NportFiling" AS filing
                    WHERE filing."ReportPeriodDate" = series."LatestReportPeriodDate"
                      AND filing."FilingDate" = series."LatestFilingDate"
                      AND (
                          (series."SeriesId" <> '' AND filing."SeriesId" = series."SeriesId")
                          OR (
                              series."SeriesId" = ''
                              AND series."CommonStockId" IS NOT NULL
                              AND filing."CommonStockId" = series."CommonStockId"
                              AND (filing."SeriesId" IS NULL OR filing."SeriesId" = '')
                          )
                          OR (
                              series."SeriesId" = ''
                              AND series."CommonStockId" IS NULL
                              AND filing."CommonStockId" IS NULL
                              AND filing."RegistrantCik" = series."RegistrantCik"
                              AND (filing."SeriesId" IS NULL OR filing."SeriesId" = '')
                          )
                      )
                    ORDER BY filing."AccessionNumber" DESC
                    LIMIT 1
                );
                """
            );

            migrationBuilder.CreateIndex(
                name: "IX_FundSeries_LatestNportFilingId",
                table: "FundSeries",
                column: "LatestNportFilingId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_FundSeries_LatestNportFilingId",
                table: "FundSeries");

            migrationBuilder.DropColumn(
                name: "LatestNportFilingId",
                table: "FundSeries");
        }
    }
}

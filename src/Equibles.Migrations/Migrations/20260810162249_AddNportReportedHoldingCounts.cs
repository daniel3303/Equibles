using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddNportReportedHoldingCounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ReportedHoldingCount",
                table: "NportFiling",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReportedHoldingCount",
                table: "FundSeries",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReportedHoldingCount",
                table: "NportFiling");

            migrationBuilder.DropColumn(
                name: "ReportedHoldingCount",
                table: "FundSeries");
        }
    }
}

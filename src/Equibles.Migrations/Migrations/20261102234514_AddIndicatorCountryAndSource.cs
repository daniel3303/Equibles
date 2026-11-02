using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddIndicatorCountryAndSource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Every series stored before this migration is a US FRED series.
            migrationBuilder.AddColumn<string>(
                name: "CountryCode",
                table: "FredSeries",
                type: "character varying(2)",
                maxLength: 2,
                nullable: false,
                defaultValue: "US");

            migrationBuilder.AddColumn<int>(
                name: "Source",
                table: "FredSeries",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "SourceKey",
                table: "FredSeries",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceUrl",
                table: "FredSeries",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_FredSeries_CountryCode",
                table: "FredSeries",
                column: "CountryCode");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_FredSeries_CountryCode",
                table: "FredSeries");

            migrationBuilder.DropColumn(
                name: "CountryCode",
                table: "FredSeries");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "FredSeries");

            migrationBuilder.DropColumn(
                name: "SourceKey",
                table: "FredSeries");

            migrationBuilder.DropColumn(
                name: "SourceUrl",
                table: "FredSeries");
        }
    }
}

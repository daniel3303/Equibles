using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class CanadianReleaseCalendar : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ReferencePeriod",
                table: "FredReleaseDate",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "ReleaseId",
                table: "FredRelease",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddColumn<string>(
                name: "CountryCode",
                table: "FredRelease",
                type: "character varying(2)",
                maxLength: 2,
                nullable: false,
                defaultValue: "US");

            migrationBuilder.AddColumn<int>(
                name: "Source",
                table: "FredRelease",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "SourceKey",
                table: "FredRelease",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_FredRelease_CountryCode",
                table: "FredRelease",
                column: "CountryCode");

            migrationBuilder.CreateIndex(
                name: "IX_FredRelease_Source_SourceKey",
                table: "FredRelease",
                columns: new[] { "Source", "SourceKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_FredRelease_CountryCode",
                table: "FredRelease");

            migrationBuilder.DropIndex(
                name: "IX_FredRelease_Source_SourceKey",
                table: "FredRelease");

            migrationBuilder.DropColumn(
                name: "ReferencePeriod",
                table: "FredReleaseDate");

            migrationBuilder.DropColumn(
                name: "CountryCode",
                table: "FredRelease");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "FredRelease");

            migrationBuilder.DropColumn(
                name: "SourceKey",
                table: "FredRelease");

            migrationBuilder.AlterColumn<int>(
                name: "ReleaseId",
                table: "FredRelease",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);
        }
    }
}

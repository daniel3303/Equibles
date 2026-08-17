using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddCongressDisclosureAssetDetail : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ParserVersion",
                table: "CongressionalFilingRecord",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "AssetType",
                table: "CongressionalDisclosureLine",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "IncomeMaximum",
                table: "CongressionalDisclosureLine",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "IncomeMinimum",
                table: "CongressionalDisclosureLine",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IncomeType",
                table: "CongressionalDisclosureLine",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ParserVersion",
                table: "CongressionalFilingRecord");

            migrationBuilder.DropColumn(
                name: "AssetType",
                table: "CongressionalDisclosureLine");

            migrationBuilder.DropColumn(
                name: "IncomeMaximum",
                table: "CongressionalDisclosureLine");

            migrationBuilder.DropColumn(
                name: "IncomeMinimum",
                table: "CongressionalDisclosureLine");

            migrationBuilder.DropColumn(
                name: "IncomeType",
                table: "CongressionalDisclosureLine");
        }
    }
}

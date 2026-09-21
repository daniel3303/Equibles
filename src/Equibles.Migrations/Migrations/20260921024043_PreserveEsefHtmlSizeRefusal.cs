using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class PreserveEsefHtmlSizeRefusal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "HtmlCeilingBytes",
                table: "EsefOversizedReport",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HtmlSourceUrl",
                table: "EsefOversizedReport",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HtmlCeilingBytes",
                table: "EsefOversizedReport");

            migrationBuilder.DropColumn(
                name: "HtmlSourceUrl",
                table: "EsefOversizedReport");
        }
    }
}

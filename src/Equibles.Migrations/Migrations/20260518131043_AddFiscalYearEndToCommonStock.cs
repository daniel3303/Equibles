using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddFiscalYearEndToCommonStock : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "FiscalYearEndDay",
                table: "CommonStock",
                type: "integer",
                nullable: true
            );

            migrationBuilder.AddColumn<int>(
                name: "FiscalYearEndMonth",
                table: "CommonStock",
                type: "integer",
                nullable: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "FiscalYearEndDay", table: "CommonStock");

            migrationBuilder.DropColumn(name: "FiscalYearEndMonth", table: "CommonStock");
        }
    }
}

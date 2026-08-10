using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddFinancialFactsImporterVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ImporterVersion",
                table: "FinancialFactsSyncStatus",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ImporterVersion",
                table: "FinancialFactsSyncStatus");
        }
    }
}

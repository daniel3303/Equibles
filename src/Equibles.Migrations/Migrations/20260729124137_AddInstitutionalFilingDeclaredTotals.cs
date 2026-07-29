using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddInstitutionalFilingDeclaredTotals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DeclaredPositionCount",
                table: "InstitutionalFiling",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "DeclaredTotalValue",
                table: "InstitutionalFiling",
                type: "bigint",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DeclaredPositionCount",
                table: "InstitutionalFiling");

            migrationBuilder.DropColumn(
                name: "DeclaredTotalValue",
                table: "InstitutionalFiling");
        }
    }
}

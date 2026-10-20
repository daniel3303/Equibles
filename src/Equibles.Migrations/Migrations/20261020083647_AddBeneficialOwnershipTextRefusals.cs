using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddBeneficialOwnershipTextRefusals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ParserVersion",
                table: "BeneficialOwnershipFiling",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RefusalReason",
                table: "BeneficialOwnershipFiling",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ParserVersion",
                table: "BeneficialOwnershipFiling");

            migrationBuilder.DropColumn(
                name: "RefusalReason",
                table: "BeneficialOwnershipFiling");
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddInstitutionalHolderClassification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Classification",
                table: "InstitutionalHolder",
                type: "integer",
                nullable: false,
                defaultValue: 0
            );

            migrationBuilder.CreateIndex(
                name: "IX_InstitutionalHolder_Classification",
                table: "InstitutionalHolder",
                column: "Classification"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_InstitutionalHolder_Classification",
                table: "InstitutionalHolder"
            );

            migrationBuilder.DropColumn(name: "Classification", table: "InstitutionalHolder");
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddCongressMemberBioguideIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BioguideId",
                table: "CongressMember",
                type: "character varying(7)",
                maxLength: 7,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_CongressMember_BioguideId",
                table: "CongressMember",
                column: "BioguideId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CongressMember_BioguideId",
                table: "CongressMember");

            migrationBuilder.DropColumn(
                name: "BioguideId",
                table: "CongressMember");
        }
    }
}

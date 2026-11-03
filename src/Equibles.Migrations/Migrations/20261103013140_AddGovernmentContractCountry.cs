using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddGovernmentContractCountry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Every contract stored before this migration is a US federal award.
            migrationBuilder.AddColumn<string>(
                name: "CountryCode",
                table: "GovernmentContract",
                type: "character varying(2)",
                maxLength: 2,
                nullable: false,
                defaultValue: "US");

            migrationBuilder.CreateIndex(
                name: "IX_GovernmentContract_CountryCode_ActionDate",
                table: "GovernmentContract",
                columns: new[] { "CountryCode", "ActionDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_GovernmentContract_CountryCode_ActionDate",
                table: "GovernmentContract");

            migrationBuilder.DropColumn(
                name: "CountryCode",
                table: "GovernmentContract");
        }
    }
}

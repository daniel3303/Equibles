using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddEquityListingWasReferenceListed : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "WasReferenceListed",
                table: "EquityListing",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Every listing the directory claims today was claimed; one it released earlier is unknown.
            migrationBuilder.Sql(
                """
                UPDATE "EquityListing" SET "WasReferenceListed" = TRUE WHERE "IsReferenceListed";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "WasReferenceListed",
                table: "EquityListing");
        }
    }
}

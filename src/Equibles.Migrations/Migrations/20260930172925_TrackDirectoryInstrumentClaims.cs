using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class TrackDirectoryInstrumentClaims : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsDirectoryListed",
                table: "EquityListingSourceIdentifier",
                type: "boolean",
                nullable: false,
                defaultValue: false);
            migrationBuilder.Sql("""
                UPDATE "EquityListingSourceIdentifier" AS identifier
                SET "IsDirectoryListed" = listing."Active" AND listing."IsDirectoryListed"
                FROM "EquityListing" AS listing
                WHERE listing."Id" = identifier."EquityListingId";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("Directory claim history cannot be discarded by rollback.");
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class ListingPresentationNavigation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_EquityIssuerPresentation_EquityListingId",
                table: "EquityIssuerPresentation");

            migrationBuilder.CreateIndex(
                name: "IX_EquityIssuerPresentation_EquityListingId",
                table: "EquityIssuerPresentation",
                column: "EquityListingId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_EquityIssuerPresentation_EquityListingId",
                table: "EquityIssuerPresentation");

            migrationBuilder.CreateIndex(
                name: "IX_EquityIssuerPresentation_EquityListingId",
                table: "EquityIssuerPresentation",
                column: "EquityListingId");
        }
    }
}

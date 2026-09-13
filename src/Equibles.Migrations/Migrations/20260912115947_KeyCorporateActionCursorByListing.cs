using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class KeyCorporateActionCursorByListing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "LastEquityListingId",
                table: "CorporateActionPriceReconciliationCursor",
                type: "uuid",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "CorporateActionPriceReconciliationCursor",
                keyColumn: "Name",
                keyValue: "CorporateActions.PriceReconciliation",
                column: "LastEquityListingId",
                value: null);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastEquityListingId",
                table: "CorporateActionPriceReconciliationCursor");
        }
    }
}

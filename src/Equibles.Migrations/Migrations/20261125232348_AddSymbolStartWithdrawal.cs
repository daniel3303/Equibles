using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddSymbolStartWithdrawal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "EarlierHolderFrom",
                table: "EquityListingSymbolStart",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "EarlierHolderThrough",
                table: "EquityListingSymbolStart",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "WithdrawnFrom",
                table: "EquityListingSymbolStart",
                type: "date",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EarlierHolderFrom",
                table: "EquityListingSymbolStart");

            migrationBuilder.DropColumn(
                name: "EarlierHolderThrough",
                table: "EquityListingSymbolStart");

            migrationBuilder.DropColumn(
                name: "WithdrawnFrom",
                table: "EquityListingSymbolStart");
        }
    }
}

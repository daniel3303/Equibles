using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddFiscalYearEndMonthChange : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "FiscalYearEndMonthBeforeChange",
                table: "EquityIssuer",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "FiscalYearEndMonthChangedAt",
                table: "EquityIssuer",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FiscalYearEndMonthBeforeChange",
                table: "EquityIssuer");

            migrationBuilder.DropColumn(
                name: "FiscalYearEndMonthChangedAt",
                table: "EquityIssuer");
        }
    }
}

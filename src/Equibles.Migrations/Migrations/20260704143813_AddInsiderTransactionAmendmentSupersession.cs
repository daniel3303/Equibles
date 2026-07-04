using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddInsiderTransactionAmendmentSupersession : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "OriginalFilingDate",
                table: "InsiderTransaction",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SupersededAccessionNumber",
                table: "InsiderTransaction",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OriginalFilingDate",
                table: "InsiderTransaction");

            migrationBuilder.DropColumn(
                name: "SupersededAccessionNumber",
                table: "InsiderTransaction");
        }
    }
}

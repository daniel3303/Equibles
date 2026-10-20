using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddInsiderOwnershipVehicle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "NatureOfOwnership",
                table: "InsiderTransaction",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<List<string>>(
                name: "OwnershipNatureFootnoteIds",
                table: "InsiderTransaction",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NatureOfOwnership",
                table: "InsiderTransaction");

            migrationBuilder.DropColumn(
                name: "OwnershipNatureFootnoteIds",
                table: "InsiderTransaction");
        }
    }
}

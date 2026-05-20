using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddSectorTaxonomy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SectorId",
                table: "Industry",
                type: "uuid",
                nullable: true
            );

            migrationBuilder.CreateTable(
                name: "Sector",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: true
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sector", x => x.Id);
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_Industry_SectorId",
                table: "Industry",
                column: "SectorId"
            );

            migrationBuilder.AddForeignKey(
                name: "FK_Industry_Sector_SectorId",
                table: "Industry",
                column: "SectorId",
                principalTable: "Sector",
                principalColumn: "Id"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(name: "FK_Industry_Sector_SectorId", table: "Industry");

            migrationBuilder.DropTable(name: "Sector");

            migrationBuilder.DropIndex(name: "IX_Industry_SectorId", table: "Industry");

            migrationBuilder.DropColumn(name: "SectorId", table: "Industry");
        }
    }
}

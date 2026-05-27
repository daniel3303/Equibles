using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddDirtyAtToAumQuarterlySnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DirtyAt",
                table: "AumQuarterlySnapshot",
                type: "timestamp with time zone",
                nullable: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_AumQuarterlySnapshot_DirtyAt",
                table: "AumQuarterlySnapshot",
                column: "DirtyAt",
                filter: "\"DirtyAt\" IS NOT NULL"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AumQuarterlySnapshot_DirtyAt",
                table: "AumQuarterlySnapshot"
            );

            migrationBuilder.DropColumn(name: "DirtyAt", table: "AumQuarterlySnapshot");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddPendingBlobDeletionAndContentHashIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PendingBlobDeletion",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ContentHash = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    RelativePath = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    QueuedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PendingBlobDeletion", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_File_ContentHash",
                table: "File",
                column: "ContentHash");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PendingBlobDeletion");

            migrationBuilder.DropIndex(
                name: "IX_File_ContentHash",
                table: "File");
        }
    }
}

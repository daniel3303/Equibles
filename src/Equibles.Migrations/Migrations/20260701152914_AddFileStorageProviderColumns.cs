using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddFileStorageProviderColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ContentHash",
                table: "File",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RelativePath",
                table: "File",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StorageProvider",
                table: "File",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true,
                defaultValue: "Database");

            migrationBuilder.CreateIndex(
                name: "IX_File_StorageProvider",
                table: "File",
                column: "StorageProvider");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_File_StorageProvider",
                table: "File");

            migrationBuilder.DropColumn(
                name: "ContentHash",
                table: "File");

            migrationBuilder.DropColumn(
                name: "RelativePath",
                table: "File");

            migrationBuilder.DropColumn(
                name: "StorageProvider",
                table: "File");
        }
    }
}

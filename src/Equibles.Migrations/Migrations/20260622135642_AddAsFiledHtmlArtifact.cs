using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddAsFiledHtmlArtifact : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AsFiledHtmlAttempts",
                table: "Document",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "AsFiledHtmlContentId",
                table: "Document",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "AsFiledHtmlUncompressedSize",
                table: "Document",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AsFiledHtmlVersion",
                table: "Document",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_Document_AsFiledHtmlContentId",
                table: "Document",
                column: "AsFiledHtmlContentId");

            migrationBuilder.CreateIndex(
                name: "IX_Document_DocumentType_AsFiledHtmlVersion",
                table: "Document",
                columns: new[] { "DocumentType", "AsFiledHtmlVersion" });

            migrationBuilder.AddForeignKey(
                name: "FK_Document_File_AsFiledHtmlContentId",
                table: "Document",
                column: "AsFiledHtmlContentId",
                principalTable: "File",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Document_File_AsFiledHtmlContentId",
                table: "Document");

            migrationBuilder.DropIndex(
                name: "IX_Document_AsFiledHtmlContentId",
                table: "Document");

            migrationBuilder.DropIndex(
                name: "IX_Document_DocumentType_AsFiledHtmlVersion",
                table: "Document");

            migrationBuilder.DropColumn(
                name: "AsFiledHtmlAttempts",
                table: "Document");

            migrationBuilder.DropColumn(
                name: "AsFiledHtmlContentId",
                table: "Document");

            migrationBuilder.DropColumn(
                name: "AsFiledHtmlUncompressedSize",
                table: "Document");

            migrationBuilder.DropColumn(
                name: "AsFiledHtmlVersion",
                table: "Document");
        }
    }
}

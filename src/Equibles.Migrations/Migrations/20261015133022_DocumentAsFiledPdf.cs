using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class DocumentAsFiledPdf : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AsFiledPdfContentId",
                table: "Document",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Document_AsFiledPdfContentId",
                table: "Document",
                column: "AsFiledPdfContentId");

            migrationBuilder.AddForeignKey(
                name: "FK_Document_File_AsFiledPdfContentId",
                table: "Document",
                column: "AsFiledPdfContentId",
                principalTable: "File",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Document_File_AsFiledPdfContentId",
                table: "Document");

            migrationBuilder.DropIndex(
                name: "IX_Document_AsFiledPdfContentId",
                table: "Document");

            migrationBuilder.DropColumn(
                name: "AsFiledPdfContentId",
                table: "Document");
        }
    }
}

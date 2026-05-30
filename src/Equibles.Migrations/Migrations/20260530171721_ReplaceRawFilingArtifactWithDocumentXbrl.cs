using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class ReplaceRawFilingArtifactWithDocumentXbrl : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "RawFilingArtifact");

            migrationBuilder.AddColumn<Guid>(
                name: "XbrlContentId",
                table: "Document",
                type: "uuid",
                nullable: true
            );

            migrationBuilder.AddColumn<int>(
                name: "XbrlStatus",
                table: "Document",
                type: "integer",
                nullable: false,
                defaultValue: 0
            );

            migrationBuilder.AddColumn<int>(
                name: "XbrlType",
                table: "Document",
                type: "integer",
                nullable: true
            );

            migrationBuilder.AddColumn<long>(
                name: "XbrlUncompressedSize",
                table: "Document",
                type: "bigint",
                nullable: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_Document_XbrlContentId",
                table: "Document",
                column: "XbrlContentId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_Document_XbrlStatus",
                table: "Document",
                column: "XbrlStatus"
            );

            migrationBuilder.AddForeignKey(
                name: "FK_Document_File_XbrlContentId",
                table: "Document",
                column: "XbrlContentId",
                principalTable: "File",
                principalColumn: "Id"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Document_File_XbrlContentId",
                table: "Document"
            );

            migrationBuilder.DropIndex(name: "IX_Document_XbrlContentId", table: "Document");

            migrationBuilder.DropIndex(name: "IX_Document_XbrlStatus", table: "Document");

            migrationBuilder.DropColumn(name: "XbrlContentId", table: "Document");

            migrationBuilder.DropColumn(name: "XbrlStatus", table: "Document");

            migrationBuilder.DropColumn(name: "XbrlType", table: "Document");

            migrationBuilder.DropColumn(name: "XbrlUncompressedSize", table: "Document");

            migrationBuilder.CreateTable(
                name: "RawFilingArtifact",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CommonStockId = table.Column<Guid>(type: "uuid", nullable: false),
                    AccessionNumber = table.Column<string>(
                        type: "character varying(32)",
                        maxLength: 32,
                        nullable: true
                    ),
                    ArtifactType = table.Column<int>(type: "integer", nullable: false),
                    CompressedSize = table.Column<long>(type: "bigint", nullable: false),
                    Content = table.Column<byte[]>(type: "bytea", nullable: true),
                    CreationTime = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    SourceFileName = table.Column<string>(
                        type: "character varying(256)",
                        maxLength: 256,
                        nullable: true
                    ),
                    UncompressedSize = table.Column<long>(type: "bigint", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RawFilingArtifact", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RawFilingArtifact_CommonStock_CommonStockId",
                        column: x => x.CommonStockId,
                        principalTable: "CommonStock",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_RawFilingArtifact_AccessionNumber_ArtifactType",
                table: "RawFilingArtifact",
                columns: new[] { "AccessionNumber", "ArtifactType" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_RawFilingArtifact_CommonStockId",
                table: "RawFilingArtifact",
                column: "CommonStockId"
            );
        }
    }
}

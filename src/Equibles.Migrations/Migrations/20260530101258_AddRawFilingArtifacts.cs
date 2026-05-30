using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddRawFilingArtifacts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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
                    SourceFileName = table.Column<string>(
                        type: "character varying(256)",
                        maxLength: 256,
                        nullable: true
                    ),
                    Content = table.Column<byte[]>(type: "bytea", nullable: true),
                    UncompressedSize = table.Column<long>(type: "bigint", nullable: false),
                    CompressedSize = table.Column<long>(type: "bigint", nullable: false),
                    CreationTime = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "RawFilingArtifact");
        }
    }
}

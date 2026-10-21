using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddProcessedDataSetAccession : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProcessedDataSetAccession",
                columns: table => new
                {
                    FileName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    AccessionNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ParserVersion = table.Column<int>(type: "integer", nullable: false),
                    ArchiveFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcessedDataSetAccession", x => new { x.FileName, x.AccessionNumber });
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProcessedDataSetAccession");
        }
    }
}

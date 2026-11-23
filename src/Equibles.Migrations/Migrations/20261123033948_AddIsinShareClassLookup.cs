using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddIsinShareClassLookup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "IsinShareClassLookup",
                columns: table => new
                {
                    Isin = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    ShareClassFigi = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: true),
                    UsTickers = table.Column<List<string>>(type: "text[]", nullable: false),
                    UsSecurityType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    QuotedOverTheCounter = table.Column<bool>(type: "boolean", nullable: false),
                    SourceRecordId = table.Column<Guid>(type: "uuid", nullable: true),
                    CheckedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    NextAttemptAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastError = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IsinShareClassLookup", x => x.Isin);
                    table.ForeignKey(
                        name: "FK_IsinShareClassLookup_EquityDirectorySourceRecord_SourceReco~",
                        column: x => x.SourceRecordId,
                        principalTable: "EquityDirectorySourceRecord",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_IsinShareClassLookup_NextAttemptAt",
                table: "IsinShareClassLookup",
                column: "NextAttemptAt");

            migrationBuilder.CreateIndex(
                name: "IX_IsinShareClassLookup_SourceRecordId",
                table: "IsinShareClassLookup",
                column: "SourceRecordId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IsinShareClassLookup");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddInsiderFilingAndSecurityKind : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ParserVersion",
                table: "InsiderTransaction",
                type: "integer",
                nullable: false,
                defaultValue: 0
            );

            migrationBuilder.AddColumn<int>(
                name: "SecurityKind",
                table: "InsiderTransaction",
                type: "integer",
                nullable: false,
                defaultValue: 0
            );

            migrationBuilder.CreateTable(
                name: "InsiderFiling",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccessionNumber = table.Column<string>(
                        type: "character varying(32)",
                        maxLength: 32,
                        nullable: false
                    ),
                    ContentId = table.Column<Guid>(type: "uuid", nullable: true),
                    UncompressedSize = table.Column<long>(type: "bigint", nullable: true),
                    CaptureStatus = table.Column<int>(type: "integer", nullable: false),
                    CaptureAttempts = table.Column<int>(type: "integer", nullable: false),
                    CreationTime = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InsiderFiling", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InsiderFiling_File_ContentId",
                        column: x => x.ContentId,
                        principalTable: "File",
                        principalColumn: "Id"
                    );
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_InsiderTransaction_ParserVersion",
                table: "InsiderTransaction",
                column: "ParserVersion"
            );

            migrationBuilder.CreateIndex(
                name: "IX_InsiderTransaction_SecurityKind_TransactionDate",
                table: "InsiderTransaction",
                columns: new[] { "SecurityKind", "TransactionDate" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_InsiderFiling_AccessionNumber",
                table: "InsiderFiling",
                column: "AccessionNumber",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_InsiderFiling_CaptureStatus",
                table: "InsiderFiling",
                column: "CaptureStatus"
            );

            migrationBuilder.CreateIndex(
                name: "IX_InsiderFiling_ContentId",
                table: "InsiderFiling",
                column: "ContentId"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "InsiderFiling");

            migrationBuilder.DropIndex(
                name: "IX_InsiderTransaction_ParserVersion",
                table: "InsiderTransaction"
            );

            migrationBuilder.DropIndex(
                name: "IX_InsiderTransaction_SecurityKind_TransactionDate",
                table: "InsiderTransaction"
            );

            migrationBuilder.DropColumn(name: "ParserVersion", table: "InsiderTransaction");

            migrationBuilder.DropColumn(name: "SecurityKind", table: "InsiderTransaction");
        }
    }
}

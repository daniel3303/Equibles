using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddIssuerCoverStatement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "IssuerCoverStatement",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EquityIssuerId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    AccessionNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Form = table.Column<string>(type: "text", nullable: false),
                    FiledDate = table.Column<DateOnly>(type: "date", nullable: false),
                    FileNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    IsShellCompany = table.Column<bool>(type: "boolean", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IssuerCoverStatement", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IssuerCoverStatement_Document_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Document",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_IssuerCoverStatement_EquityIssuer_EquityIssuerId",
                        column: x => x.EquityIssuerId,
                        principalTable: "EquityIssuer",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_IssuerCoverStatement_DocumentId",
                table: "IssuerCoverStatement",
                column: "DocumentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IssuerCoverStatement_EquityIssuerId_FiledDate",
                table: "IssuerCoverStatement",
                columns: new[] { "EquityIssuerId", "FiledDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IssuerCoverStatement");
        }
    }
}

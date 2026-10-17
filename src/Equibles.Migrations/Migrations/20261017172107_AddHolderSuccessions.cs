using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddHolderSuccessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "HolderSuccession",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PredecessorHolderId = table.Column<Guid>(type: "uuid", nullable: false),
                    SuccessorHolderId = table.Column<Guid>(type: "uuid", nullable: false),
                    PredecessorLastReportDate = table.Column<DateOnly>(type: "date", nullable: false),
                    SuccessorFirstReportDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Basis = table.Column<int>(type: "integer", nullable: false),
                    EvidenceAccession = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    DerivationVersion = table.Column<int>(type: "integer", nullable: false),
                    ComputedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HolderSuccession", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HolderSuccession_InstitutionalHolder_PredecessorHolderId",
                        column: x => x.PredecessorHolderId,
                        principalTable: "InstitutionalHolder",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_HolderSuccession_InstitutionalHolder_SuccessorHolderId",
                        column: x => x.SuccessorHolderId,
                        principalTable: "InstitutionalHolder",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HolderSuccession_PredecessorHolderId_SuccessorHolderId",
                table: "HolderSuccession",
                columns: new[] { "PredecessorHolderId", "SuccessorHolderId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HolderSuccession_SuccessorHolderId",
                table: "HolderSuccession",
                column: "SuccessorHolderId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HolderSuccession");
        }
    }
}

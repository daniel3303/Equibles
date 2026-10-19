using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddHolderQuarterlySectorFlow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "HolderQuarterlySectorFlow",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InstitutionalHolderId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReportDate = table.Column<DateOnly>(type: "date", nullable: false),
                    SectorGroup = table.Column<int>(type: "integer", nullable: false),
                    SectorId = table.Column<Guid>(type: "uuid", nullable: true),
                    BuyValue = table.Column<decimal>(type: "numeric(20,2)", precision: 20, scale: 2, nullable: false),
                    SellValue = table.Column<decimal>(type: "numeric(20,2)", precision: 20, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HolderQuarterlySectorFlow", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HolderQuarterlySectorFlow_InstitutionalHolder_Institutional~",
                        column: x => x.InstitutionalHolderId,
                        principalTable: "InstitutionalHolder",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_HolderQuarterlySectorFlow_Sector_SectorId",
                        column: x => x.SectorId,
                        principalTable: "Sector",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HolderQuarterlySectorFlow_InstitutionalHolderId_ReportDate_~",
                table: "HolderQuarterlySectorFlow",
                columns: new[] { "InstitutionalHolderId", "ReportDate", "SectorGroup", "SectorId" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_HolderQuarterlySectorFlow_ReportDate",
                table: "HolderQuarterlySectorFlow",
                column: "ReportDate");

            migrationBuilder.CreateIndex(
                name: "IX_HolderQuarterlySectorFlow_SectorId",
                table: "HolderQuarterlySectorFlow",
                column: "SectorId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HolderQuarterlySectorFlow");
        }
    }
}

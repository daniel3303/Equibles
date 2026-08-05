using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class PerListing13FIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_InstitutionalHolding_CommonStockId_InstitutionalHolderId_Re~",
                table: "InstitutionalHolding");

            migrationBuilder.AddColumn<string>(
                name: "ListedTicker",
                table: "InstitutionalHolding",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CommonStockListedCusip",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CommonStockId = table.Column<Guid>(type: "uuid", nullable: false),
                    ListedTicker = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Cusip = table.Column<string>(type: "character varying(9)", maxLength: 9, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommonStockListedCusip", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CommonStockListedCusip_CommonStock_CommonStockId",
                        column: x => x.CommonStockId,
                        principalTable: "CommonStock",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InstitutionalHolding_CommonStockId_InstitutionalHolderId_Re~",
                table: "InstitutionalHolding",
                columns: new[] { "CommonStockId", "InstitutionalHolderId", "ReportDate", "ShareType", "OptionType", "FilingType", "ListedTicker" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_CommonStockListedCusip_CommonStockId",
                table: "CommonStockListedCusip",
                column: "CommonStockId");

            migrationBuilder.CreateIndex(
                name: "IX_CommonStockListedCusip_Cusip",
                table: "CommonStockListedCusip",
                column: "Cusip",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CommonStockListedCusip");

            migrationBuilder.DropIndex(
                name: "IX_InstitutionalHolding_CommonStockId_InstitutionalHolderId_Re~",
                table: "InstitutionalHolding");

            migrationBuilder.DropColumn(
                name: "ListedTicker",
                table: "InstitutionalHolding");

            migrationBuilder.CreateIndex(
                name: "IX_InstitutionalHolding_CommonStockId_InstitutionalHolderId_Re~",
                table: "InstitutionalHolding",
                columns: new[] { "CommonStockId", "InstitutionalHolderId", "ReportDate", "ShareType", "OptionType", "FilingType" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);
        }
    }
}

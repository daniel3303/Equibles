using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddCongressionalTradeMetadataAndImportPartitions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AssetType",
                table: "CongressionalTrade",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Subholding",
                table: "CongressionalTrade",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "CongressionalTradeImportPartition",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Year = table.Column<int>(type: "integer", nullable: false),
                    ParserVersion = table.Column<int>(type: "integer", nullable: false),
                    FilingCount = table.Column<int>(type: "integer", nullable: false),
                    TransactionCount = table.Column<int>(type: "integer", nullable: false),
                    CompletionTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CongressionalTradeImportPartition", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CongressionalTradeImportPartition_Kind_Year",
                table: "CongressionalTradeImportPartition",
                columns: new[] { "Kind", "Year" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CongressionalTradeImportPartition");

            migrationBuilder.DropColumn(
                name: "AssetType",
                table: "CongressionalTrade");

            migrationBuilder.DropColumn(
                name: "Subholding",
                table: "CongressionalTrade");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddListedSecurityClassification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ListedSecurityTitle",
                table: "CommonStock",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ListedSecurityType",
                table: "CommonStock",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "ListedSecurity",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CommonStockId = table.Column<Guid>(type: "uuid", nullable: false),
                    TradingSymbol = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ExchangeName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    AccessionNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    FiledDate = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ListedSecurity", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ListedSecurity_CommonStock_CommonStockId",
                        column: x => x.CommonStockId,
                        principalTable: "CommonStock",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ListedSecurity_CommonStockId_TradingSymbol",
                table: "ListedSecurity",
                columns: new[] { "CommonStockId", "TradingSymbol" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ListedSecurity");

            migrationBuilder.DropColumn(
                name: "ListedSecurityTitle",
                table: "CommonStock");

            migrationBuilder.DropColumn(
                name: "ListedSecurityType",
                table: "CommonStock");
        }
    }
}

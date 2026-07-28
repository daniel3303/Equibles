using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddCommonStockTickerAlias : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CommonStockTickerAlias",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CommonStockId = table.Column<Guid>(type: "uuid", nullable: false),
                    Ticker = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommonStockTickerAlias", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CommonStockTickerAlias_CommonStock_CommonStockId",
                        column: x => x.CommonStockId,
                        principalTable: "CommonStock",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CommonStockTickerAlias_CommonStockId",
                table: "CommonStockTickerAlias",
                column: "CommonStockId");

            migrationBuilder.CreateIndex(
                name: "IX_CommonStockTickerAlias_Ticker",
                table: "CommonStockTickerAlias",
                column: "Ticker",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CommonStockTickerAlias");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddCommonStockCusipAlias : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CommonStockCusipAlias",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CommonStockId = table.Column<Guid>(type: "uuid", nullable: false),
                    Cusip = table.Column<string>(type: "character varying(9)", maxLength: 9, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommonStockCusipAlias", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CommonStockCusipAlias_CommonStock_CommonStockId",
                        column: x => x.CommonStockId,
                        principalTable: "CommonStock",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CommonStockCusipAlias_CommonStockId",
                table: "CommonStockCusipAlias",
                column: "CommonStockId");

            migrationBuilder.CreateIndex(
                name: "IX_CommonStockCusipAlias_Cusip",
                table: "CommonStockCusipAlias",
                column: "Cusip",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CommonStockCusipAlias");
        }
    }
}

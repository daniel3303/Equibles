using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddNportTrustSweepCoverage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_NportFiling_CommonStock_CommonStockId",
                table: "NportFiling");

            migrationBuilder.AlterColumn<Guid>(
                name: "CommonStockId",
                table: "NportFiling",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<string>(
                name: "RegistrantCik",
                table: "NportFiling",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ProcessedNportFiling",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccessionNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcessedNportFiling", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NportFiling_RegistrantCik",
                table: "NportFiling",
                column: "RegistrantCik");

            migrationBuilder.CreateIndex(
                name: "IX_ProcessedNportFiling_AccessionNumber",
                table: "ProcessedNportFiling",
                column: "AccessionNumber",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_NportFiling_CommonStock_CommonStockId",
                table: "NportFiling",
                column: "CommonStockId",
                principalTable: "CommonStock",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_NportFiling_CommonStock_CommonStockId",
                table: "NportFiling");

            migrationBuilder.DropTable(
                name: "ProcessedNportFiling");

            migrationBuilder.DropIndex(
                name: "IX_NportFiling_RegistrantCik",
                table: "NportFiling");

            migrationBuilder.DropColumn(
                name: "RegistrantCik",
                table: "NportFiling");

            migrationBuilder.AlterColumn<Guid>(
                name: "CommonStockId",
                table: "NportFiling",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_NportFiling_CommonStock_CommonStockId",
                table: "NportFiling",
                column: "CommonStockId",
                principalTable: "CommonStock",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}

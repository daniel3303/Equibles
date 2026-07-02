using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class DropInvestorRelationsDiscoveryColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "InvestorRelationsCheckedAt",
                table: "CommonStock");

            migrationBuilder.DropColumn(
                name: "InvestorRelationsDiscoveryVersion",
                table: "CommonStock");

            migrationBuilder.DropColumn(
                name: "InvestorRelationsRetryAfter",
                table: "CommonStock");

            migrationBuilder.DropColumn(
                name: "InvestorRelationsUrl",
                table: "CommonStock");

            migrationBuilder.DropColumn(
                name: "IrContentScrapedAt",
                table: "CommonStock");

            migrationBuilder.DropColumn(
                name: "IrPlatformType",
                table: "CommonStock");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "InvestorRelationsCheckedAt",
                table: "CommonStock",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "InvestorRelationsDiscoveryVersion",
                table: "CommonStock",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "InvestorRelationsRetryAfter",
                table: "CommonStock",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InvestorRelationsUrl",
                table: "CommonStock",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "IrContentScrapedAt",
                table: "CommonStock",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "IrPlatformType",
                table: "CommonStock",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }
    }
}

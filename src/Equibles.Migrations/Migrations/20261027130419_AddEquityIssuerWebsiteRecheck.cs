using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddEquityIssuerWebsiteRecheck : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "WebsiteDiscoveryVersion",
                table: "EquityIssuer",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "WebsiteRecheckProbeFailedAt",
                table: "EquityIssuer",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "WebsiteDiscoveryVersion",
                table: "EquityIssuer");

            migrationBuilder.DropColumn(
                name: "WebsiteRecheckProbeFailedAt",
                table: "EquityIssuer");
        }
    }
}

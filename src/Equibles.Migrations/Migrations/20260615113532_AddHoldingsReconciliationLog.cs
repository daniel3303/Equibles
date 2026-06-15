using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddHoldingsReconciliationLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "HoldingsReconciliationLog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    InstitutionalHolderId = table.Column<Guid>(type: "uuid", nullable: false),
                    HolderName = table.Column<string>(type: "text", nullable: true),
                    HolderCik = table.Column<string>(type: "text", nullable: true),
                    Outcome = table.Column<int>(type: "integer", nullable: false),
                    QuartersReingested = table.Column<int>(type: "integer", nullable: false),
                    Details = table.Column<string>(type: "text", nullable: true),
                    TriggeredBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HoldingsReconciliationLog", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HoldingsReconciliationLog_CreationTime",
                table: "HoldingsReconciliationLog",
                column: "CreationTime");

            migrationBuilder.CreateIndex(
                name: "IX_HoldingsReconciliationLog_InstitutionalHolderId",
                table: "HoldingsReconciliationLog",
                column: "InstitutionalHolderId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HoldingsReconciliationLog");
        }
    }
}

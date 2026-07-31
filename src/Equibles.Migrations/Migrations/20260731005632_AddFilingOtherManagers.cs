using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddFilingOtherManagers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FilingOtherManager",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccessionNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Direction = table.Column<int>(type: "integer", nullable: false),
                    SequenceNumber = table.Column<int>(type: "integer", nullable: false),
                    Cik = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    Form13FFileNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    CrdNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    SecFileNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    CreationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FilingOtherManager", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FilingOtherManager_AccessionNumber",
                table: "FilingOtherManager",
                column: "AccessionNumber");

            migrationBuilder.CreateIndex(
                name: "IX_FilingOtherManager_Cik",
                table: "FilingOtherManager",
                column: "Cik");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FilingOtherManager");
        }
    }
}

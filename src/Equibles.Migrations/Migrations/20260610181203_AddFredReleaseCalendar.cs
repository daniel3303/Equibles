using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddFredReleaseCalendar : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "FredReleaseId",
                table: "FredSeries",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "FredRelease",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReleaseId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Link = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    PressRelease = table.Column<bool>(type: "boolean", nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FredRelease", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FredReleaseDate",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FredReleaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FredReleaseDate", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FredReleaseDate_FredRelease_FredReleaseId",
                        column: x => x.FredReleaseId,
                        principalTable: "FredRelease",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FredSeries_FredReleaseId",
                table: "FredSeries",
                column: "FredReleaseId");

            migrationBuilder.CreateIndex(
                name: "IX_FredRelease_ReleaseId",
                table: "FredRelease",
                column: "ReleaseId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FredReleaseDate_Date",
                table: "FredReleaseDate",
                column: "Date");

            migrationBuilder.CreateIndex(
                name: "IX_FredReleaseDate_FredReleaseId_Date",
                table: "FredReleaseDate",
                columns: new[] { "FredReleaseId", "Date" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_FredSeries_FredRelease_FredReleaseId",
                table: "FredSeries",
                column: "FredReleaseId",
                principalTable: "FredRelease",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_FredSeries_FredRelease_FredReleaseId",
                table: "FredSeries");

            migrationBuilder.DropTable(
                name: "FredReleaseDate");

            migrationBuilder.DropTable(
                name: "FredRelease");

            migrationBuilder.DropIndex(
                name: "IX_FredSeries_FredReleaseId",
                table: "FredSeries");

            migrationBuilder.DropColumn(
                name: "FredReleaseId",
                table: "FredSeries");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddSecTickerDirectoryObservation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SecTickerDirectoryObservation",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Cik = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Ticker = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Exchange = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    FirstObservedOn = table.Column<DateOnly>(type: "date", nullable: false),
                    LastObservedOn = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SecTickerDirectoryObservation", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SecTickerDirectoryObservation_LastObservedOn",
                table: "SecTickerDirectoryObservation",
                column: "LastObservedOn");

            migrationBuilder.CreateIndex(
                name: "IX_SecTickerDirectoryObservation_Span",
                table: "SecTickerDirectoryObservation",
                columns: new[] { "Cik", "Ticker", "Exchange", "FirstObservedOn" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SecTickerDirectoryObservation_Ticker_LastObservedOn",
                table: "SecTickerDirectoryObservation",
                columns: new[] { "Ticker", "LastObservedOn" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SecTickerDirectoryObservation");
        }
    }
}

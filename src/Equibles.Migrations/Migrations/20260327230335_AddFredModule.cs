using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddFredModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FredSeries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SeriesId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Category = table.Column<int>(type: "integer", nullable: false),
                    Frequency = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Units = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    SeasonalAdjustment = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ObservationStart = table.Column<DateOnly>(type: "date", nullable: true),
                    ObservationEnd = table.Column<DateOnly>(type: "date", nullable: true),
                    LastUpdated = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FredSeries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FredObservation",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FredSeriesId = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Value = table.Column<decimal>(type: "numeric", nullable: true),
                    CreationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FredObservation", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FredObservation_FredSeries_FredSeriesId",
                        column: x => x.FredSeriesId,
                        principalTable: "FredSeries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FredObservation_Date",
                table: "FredObservation",
                column: "Date");

            migrationBuilder.CreateIndex(
                name: "IX_FredObservation_FredSeriesId_Date",
                table: "FredObservation",
                columns: new[] { "FredSeriesId", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FredSeries_Category",
                table: "FredSeries",
                column: "Category");

            migrationBuilder.CreateIndex(
                name: "IX_FredSeries_SeriesId",
                table: "FredSeries",
                column: "SeriesId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FredObservation");

            migrationBuilder.DropTable(
                name: "FredSeries");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddEquityMarkets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EquityMarketRegistration",
                columns: table => new
                {
                    Code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    DirectoryRefreshRequestedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DirectoryRefreshedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DirectoryListingCount = table.Column<int>(type: "integer", nullable: false),
                    DirectoryImportedCount = table.Column<int>(type: "integer", nullable: false),
                    DirectoryCurrentCount = table.Column<int>(type: "integer", nullable: false),
                    DirectorySkippedCount = table.Column<int>(type: "integer", nullable: false),
                    DirectoryFailedCount = table.Column<int>(type: "integer", nullable: false),
                    LastError = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EquityMarketRegistration", x => x.Code);
                });

            migrationBuilder.CreateTable(
                name: "FirdsImportRun",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Authority = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    FileName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PublishedOn = table.Column<DateOnly>(type: "date", nullable: false),
                    Checksum = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ImportedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RowsRead = table.Column<int>(type: "integer", nullable: false),
                    RowsStored = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FirdsImportRun", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FirdsInstrumentRecord",
                columns: table => new
                {
                    Authority = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    Isin = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    Mic = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    Lei = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Cfi = table.Column<string>(type: "character varying(6)", maxLength: 6, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    FullName = table.Column<string>(type: "character varying(350)", maxLength: 350, nullable: true),
                    ShortName = table.Column<string>(type: "character varying(35)", maxLength: 35, nullable: true),
                    FirstTradeDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    TerminationDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RelevantCompetentAuthority = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true),
                    RelevantTradingVenue = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: true),
                    ObservedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RemovedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FirdsInstrumentRecord", x => new { x.Authority, x.Isin, x.Mic });
                });

            migrationBuilder.CreateIndex(
                name: "IX_FirdsImportRun_Authority_FileName",
                table: "FirdsImportRun",
                columns: new[] { "Authority", "FileName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FirdsImportRun_Authority_Kind_PublishedOn",
                table: "FirdsImportRun",
                columns: new[] { "Authority", "Kind", "PublishedOn" });

            migrationBuilder.CreateIndex(
                name: "IX_FirdsInstrumentRecord_Authority_RelevantTradingVenue",
                table: "FirdsInstrumentRecord",
                columns: new[] { "Authority", "RelevantTradingVenue" });

            migrationBuilder.CreateIndex(
                name: "IX_FirdsInstrumentRecord_Isin",
                table: "FirdsInstrumentRecord",
                column: "Isin");

            migrationBuilder.CreateIndex(
                name: "IX_FirdsInstrumentRecord_Lei",
                table: "FirdsInstrumentRecord",
                column: "Lei");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EquityMarketRegistration");

            migrationBuilder.DropTable(
                name: "FirdsImportRun");

            migrationBuilder.DropTable(
                name: "FirdsInstrumentRecord");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddDelayedTrades : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DelayedTradesCapturedAt",
                table: "EquityMarketRegistration",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DelayedTradesLastError",
                table: "EquityMarketRegistration",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "DelayedTradesSessionDate",
                table: "EquityMarketRegistration",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "YahooPriceSyncAttemptedAt",
                table: "EquityListing",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DelayedTradeFileCapture",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceKey = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    LocationCode = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    MarketCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Window = table.Column<int>(type: "integer", nullable: false),
                    Outcome = table.Column<int>(type: "integer", nullable: false),
                    SourceUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    TermsUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Bytes = table.Column<long>(type: "bigint", nullable: false),
                    Rows = table.Column<int>(type: "integer", nullable: false),
                    SessionDate = table.Column<DateOnly>(type: "date", nullable: true),
                    FetchedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DelayedTradeFileCapture", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DelayedTradeImportPartition",
                columns: table => new
                {
                    Dataset = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PartitionDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ScopeKey = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    ImportedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RederivedCount = table.Column<int>(type: "integer", nullable: false),
                    SourceUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    TermsUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    FileSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    FileBytes = table.Column<long>(type: "bigint", nullable: false),
                    PrintCount = table.Column<int>(type: "integer", nullable: false),
                    LitPrintCount = table.Column<int>(type: "integer", nullable: false),
                    DarkPrintCount = table.Column<int>(type: "integer", nullable: false),
                    CancelledCount = table.Column<int>(type: "integer", nullable: false),
                    AmendedCount = table.Column<int>(type: "integer", nullable: false),
                    DuplicateCount = table.Column<int>(type: "integer", nullable: false),
                    OutOfSessionCount = table.Column<int>(type: "integer", nullable: false),
                    IsinCount = table.Column<int>(type: "integer", nullable: false),
                    MatchedCount = table.Column<int>(type: "integer", nullable: false),
                    UnmatchedCount = table.Column<int>(type: "integer", nullable: false),
                    AmbiguousCount = table.Column<int>(type: "integer", nullable: false),
                    CurrencyMismatchCount = table.Column<int>(type: "integer", nullable: false),
                    BarsInserted = table.Column<int>(type: "integer", nullable: false),
                    BarsOverwroteYahoo = table.Column<int>(type: "integer", nullable: false),
                    BarsRederived = table.Column<int>(type: "integer", nullable: false),
                    BarsSkippedBasis = table.Column<int>(type: "integer", nullable: false),
                    BarsSkippedInvalid = table.Column<int>(type: "integer", nullable: false),
                    BarsSkippedIdentity = table.Column<int>(type: "integer", nullable: false),
                    BarsUnsettled = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DelayedTradeImportPartition", x => new { x.Dataset, x.PartitionDate, x.ScopeKey });
                });

            migrationBuilder.CreateTable(
                name: "LatestDelayedTrade",
                columns: table => new
                {
                    EquityListingId = table.Column<Guid>(type: "uuid", nullable: false),
                    Isin = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    Mic = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    SourceKey = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    MarketCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SessionDate = table.Column<DateOnly>(type: "date", nullable: false),
                    LastPrice = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    LastQuantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    LastTradedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastPublishedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastTradeId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    SessionOpen = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    SessionHigh = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    SessionLow = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    Volume = table.Column<long>(type: "bigint", nullable: false),
                    LitVolume = table.Column<long>(type: "bigint", nullable: false),
                    PrintCount = table.Column<int>(type: "integer", nullable: false),
                    IsSessionComplete = table.Column<bool>(type: "boolean", nullable: false),
                    SourceUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    TermsUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    FileSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CapturedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LatestDelayedTrade", x => x.EquityListingId);
                    table.ForeignKey(
                        name: "FK_LatestDelayedTrade_EquityListing_EquityListingId",
                        column: x => x.EquityListingId,
                        principalTable: "EquityListing",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DelayedTradeFileCapture_FetchedAtUtc",
                table: "DelayedTradeFileCapture",
                column: "FetchedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_DelayedTradeFileCapture_MarketCode_FetchedAtUtc",
                table: "DelayedTradeFileCapture",
                columns: new[] { "MarketCode", "FetchedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_DelayedTradeImportPartition_Dataset_ScopeKey_PartitionDate",
                table: "DelayedTradeImportPartition",
                columns: new[] { "Dataset", "ScopeKey", "PartitionDate" });

            migrationBuilder.CreateIndex(
                name: "IX_LatestDelayedTrade_MarketCode",
                table: "LatestDelayedTrade",
                column: "MarketCode");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DelayedTradeFileCapture");

            migrationBuilder.DropTable(
                name: "DelayedTradeImportPartition");

            migrationBuilder.DropTable(
                name: "LatestDelayedTrade");

            migrationBuilder.DropColumn(
                name: "DelayedTradesCapturedAt",
                table: "EquityMarketRegistration");

            migrationBuilder.DropColumn(
                name: "DelayedTradesLastError",
                table: "EquityMarketRegistration");

            migrationBuilder.DropColumn(
                name: "DelayedTradesSessionDate",
                table: "EquityMarketRegistration");

            migrationBuilder.DropColumn(
                name: "YahooPriceSyncAttemptedAt",
                table: "EquityListing");
        }
    }
}

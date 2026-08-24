using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddDelistedCommonStockListings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CommonStockDelistedListing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CommonStockId = table.Column<Guid>(type: "uuid", nullable: false),
                    ListedTicker = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    DelistedOn = table.Column<DateOnly>(type: "date", nullable: false),
                    HistoricalPriceBackfillAttemptedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Cusip = table.Column<string>(type: "character varying(9)", maxLength: 9, nullable: true),
                    HistoricalCusipBackfillRequestedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    HistoricalCusipBackfillCandidates = table.Column<List<string>>(type: "text[]", nullable: true),
                    HistoricalCusipBackfillCandidateOn = table.Column<DateOnly>(type: "date", nullable: true),
                    HistoricalCusipBackfillAmbiguous = table.Column<bool>(type: "boolean", nullable: false),
                    HistoricalCusipBackfillSweepStartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommonStockDelistedListing", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CommonStockDelistedListing_CommonStock_CommonStockId",
                        column: x => x.CommonStockId,
                        principalTable: "CommonStock",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CommonStockDelistedListing_CommonStockId_ListedTicker",
                table: "CommonStockDelistedListing",
                columns: new[] { "CommonStockId", "ListedTicker" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CommonStockDelistedListing_ListedTicker_DelistedOn",
                table: "CommonStockDelistedListing",
                columns: new[] { "ListedTicker", "DelistedOn" });

            migrationBuilder.Sql(
                """
                INSERT INTO "CommonStockDelistedListing" (
                    "Id",
                    "CommonStockId",
                    "ListedTicker",
                    "DelistedOn",
                    "HistoricalPriceBackfillAttemptedAt",
                    "Cusip",
                    "HistoricalCusipBackfillRequestedAt",
                    "HistoricalCusipBackfillCandidates",
                    "HistoricalCusipBackfillCandidateOn",
                    "HistoricalCusipBackfillAmbiguous",
                    "HistoricalCusipBackfillSweepStartedAt")
                SELECT
                    gen_random_uuid(),
                    "Id",
                    "Ticker",
                    "DelistedOn",
                    "HistoricalPriceBackfillAttemptedAt",
                    "Cusip",
                    "HistoricalCusipBackfillRequestedAt",
                    "HistoricalCusipBackfillCandidates",
                    "HistoricalCusipBackfillCandidateOn",
                    "HistoricalCusipBackfillAmbiguous",
                    "HistoricalCusipBackfillSweepStartedAt"
                FROM "CommonStock"
                WHERE NOT "Active" AND "DelistedOn" IS NOT NULL;
                """
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1
                        FROM "CommonStockDelistedListing" listing
                        JOIN "CommonStock" stock ON stock."Id" = listing."CommonStockId"
                        WHERE stock."Active"
                           OR listing."ListedTicker" <> stock."Ticker"
                           OR listing."DelistedOn" IS DISTINCT FROM stock."DelistedOn"
                           OR listing."Cusip" IS DISTINCT FROM stock."Cusip")
                       OR EXISTS (
                        SELECT 1
                        FROM "CommonStock"
                        WHERE "Ticker" IS NOT NULL
                        GROUP BY "Ticker"
                        HAVING COUNT(*) > 1)
                    THEN
                        RAISE EXCEPTION 'cannot remove per-listing delisting history: exact listing identity or ticker-reuse protection would be lost';
                    END IF;
                END $$;

                UPDATE "CommonStock" stock
                SET
                    "HistoricalPriceBackfillAttemptedAt" = listing."HistoricalPriceBackfillAttemptedAt",
                    "HistoricalCusipBackfillRequestedAt" = listing."HistoricalCusipBackfillRequestedAt",
                    "HistoricalCusipBackfillCandidates" = listing."HistoricalCusipBackfillCandidates",
                    "HistoricalCusipBackfillCandidateOn" = listing."HistoricalCusipBackfillCandidateOn",
                    "HistoricalCusipBackfillAmbiguous" = listing."HistoricalCusipBackfillAmbiguous",
                    "HistoricalCusipBackfillSweepStartedAt" = listing."HistoricalCusipBackfillSweepStartedAt"
                FROM "CommonStockDelistedListing" listing
                WHERE listing."CommonStockId" = stock."Id"
                  AND listing."ListedTicker" = stock."Ticker";
                """
            );

            migrationBuilder.DropTable(
                name: "CommonStockDelistedListing");
        }
    }
}

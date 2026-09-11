using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class PopulateNativeEquityPrices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EquityDailyStockPrice",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EquityListingId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceTicker = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Open = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    High = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    Low = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    Close = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    AdjustedClose = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    Volume = table.Column<long>(type: "bigint", nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EquityDailyStockPrice", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EquityDailyStockPrice_EquityListing_EquityListingId",
                        column: x => x.EquityListingId,
                        principalTable: "EquityListing",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UnattributedDailyStockPrice",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EquityIssuerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Open = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    High = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    Low = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    Close = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    AdjustedClose = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    Volume = table.Column<long>(type: "bigint", nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UnattributedDailyStockPrice", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UnattributedDailyStockPrice_EquityIssuer_EquityIssuerId",
                        column: x => x.EquityIssuerId,
                        principalTable: "EquityIssuer",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EquityDailyStockPrice_Date",
                table: "EquityDailyStockPrice",
                column: "Date");

            migrationBuilder.CreateIndex(
                name: "IX_EquityDailyStockPrice_EquityListingId_Date",
                table: "EquityDailyStockPrice",
                columns: new[] { "EquityListingId", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UnattributedDailyStockPrice_Date",
                table: "UnattributedDailyStockPrice",
                column: "Date");

            migrationBuilder.CreateIndex(
                name: "IX_UnattributedDailyStockPrice_EquityIssuerId_Date",
                table: "UnattributedDailyStockPrice",
                columns: new[] { "EquityIssuerId", "Date" },
                unique: true);
            migrationBuilder.Sql("""
                -- Temporary writers keep old binaries consistent until every consumer has moved.
                -- Creating these triggers first blocks source writes until this transaction commits.
                CREATE FUNCTION eq_sync_native_daily_price() RETURNS trigger LANGUAGE plpgsql AS $body$
                DECLARE
                    owner_id uuid;
                    listing_id uuid;
                BEGIN
                    IF TG_TABLE_NAME = 'DailyStockPrice' THEN
                        IF TG_OP = 'DELETE' THEN
                            DELETE FROM "UnattributedDailyStockPrice" WHERE "Id" = OLD."Id";
                            RETURN OLD;
                        END IF;
                        SELECT "Id" INTO STRICT owner_id FROM "EquityIssuer" WHERE "CommonStockId" = NEW."CommonStockId";
                        INSERT INTO "UnattributedDailyStockPrice" ("Id", "EquityIssuerId", "Date", "Open", "High", "Low", "Close", "AdjustedClose", "Volume", "CreationTime")
                        VALUES (NEW."Id", owner_id, NEW."Date", NEW."Open", NEW."High", NEW."Low", NEW."Close", NEW."AdjustedClose", NEW."Volume", NEW."CreationTime")
                        ON CONFLICT ("Id") DO UPDATE SET
                            "EquityIssuerId" = EXCLUDED."EquityIssuerId", "Date" = EXCLUDED."Date",
                            "Open" = EXCLUDED."Open", "High" = EXCLUDED."High", "Low" = EXCLUDED."Low",
                            "Close" = EXCLUDED."Close", "AdjustedClose" = EXCLUDED."AdjustedClose",
                            "Volume" = EXCLUDED."Volume", "CreationTime" = EXCLUDED."CreationTime";
                    ELSE
                        IF TG_OP = 'DELETE' THEN
                            DELETE FROM "EquityDailyStockPrice" WHERE "Id" = OLD."Id";
                            RETURN OLD;
                        END IF;
                        listing_id := eq_ensure_legacy_listing(NEW."CommonStockId", NEW."ListedTicker");
                        IF listing_id IS NULL THEN
                            RAISE EXCEPTION 'Exact daily bar % has no listing identity', NEW."Id";
                        END IF;
                        INSERT INTO "EquityDailyStockPrice" ("Id", "EquityListingId", "SourceTicker", "Date", "Open", "High", "Low", "Close", "AdjustedClose", "Volume", "CreationTime")
                        VALUES (NEW."Id", listing_id, NEW."ListedTicker", NEW."Date", NEW."Open", NEW."High", NEW."Low", NEW."Close", NEW."AdjustedClose", NEW."Volume", NEW."CreationTime")
                        ON CONFLICT ("Id") DO UPDATE SET
                            "EquityListingId" = EXCLUDED."EquityListingId", "SourceTicker" = EXCLUDED."SourceTicker", "Date" = EXCLUDED."Date",
                            "Open" = EXCLUDED."Open", "High" = EXCLUDED."High", "Low" = EXCLUDED."Low",
                            "Close" = EXCLUDED."Close", "AdjustedClose" = EXCLUDED."AdjustedClose",
                            "Volume" = EXCLUDED."Volume", "CreationTime" = EXCLUDED."CreationTime";
                    END IF;
                    RETURN NEW;
                END;
                $body$;
                CREATE TRIGGER equity_native_daily_price_write AFTER INSERT OR UPDATE OR DELETE ON "ListedDailyStockPrice"
                FOR EACH ROW EXECUTE FUNCTION eq_sync_native_daily_price();
                CREATE TRIGGER equity_native_unattributed_price_write AFTER INSERT OR UPDATE OR DELETE ON "DailyStockPrice"
                FOR EACH ROW EXECUTE FUNCTION eq_sync_native_daily_price();
                
                INSERT INTO "UnattributedDailyStockPrice" ("Id", "EquityIssuerId", "Date", "Open", "High", "Low", "Close", "AdjustedClose", "Volume", "CreationTime")
                SELECT p."Id", i."Id", p."Date", p."Open", p."High", p."Low", p."Close", p."AdjustedClose", p."Volume", p."CreationTime"
                FROM "DailyStockPrice" p JOIN "EquityIssuer" i ON i."CommonStockId" = p."CommonStockId";
                INSERT INTO "EquityDailyStockPrice" ("Id", "EquityListingId", "SourceTicker", "Date", "Open", "High", "Low", "Close", "AdjustedClose", "Volume", "CreationTime")
                SELECT p."Id", l."EquityListingId", p."ListedTicker", p."Date", p."Open", p."High", p."Low", p."Close", p."AdjustedClose", p."Volume", p."CreationTime"
                FROM "ListedDailyStockPrice" p JOIN "LegacyEquityListing" l
                ON l."CommonStockId" = p."CommonStockId" AND l."ListedTicker" = p."ListedTicker";
                DO $body$
                BEGIN
                    IF (SELECT count(*) FROM "DailyStockPrice") <> (SELECT count(*) FROM "UnattributedDailyStockPrice")
                       OR (SELECT count(*) FROM "ListedDailyStockPrice") <> (SELECT count(*) FROM "EquityDailyStockPrice") THEN
                        RAISE EXCEPTION 'Native price migration did not preserve every source observation';
                    END IF;
                END;
                $body$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("Native price observations must be reconciled explicitly; an automatic downgrade would discard history.");
        }
    }
}

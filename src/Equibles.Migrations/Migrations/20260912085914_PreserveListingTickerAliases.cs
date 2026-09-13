using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class PreserveListingTickerAliases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EquityListingTickerAlias",
                columns: table => new
                {
                    EquityListingId = table.Column<Guid>(type: "uuid", nullable: false),
                    Ticker = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    EvidenceSource = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RecordedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EquityListingTickerAlias", x => new { x.EquityListingId, x.Ticker });
                    table.ForeignKey(
                        name: "FK_EquityListingTickerAlias_EquityListing_EquityListingId",
                        column: x => x.EquityListingId,
                        principalTable: "EquityListing",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });
            migrationBuilder.Sql("""
                DO $audit$
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM "LegacyEquityListing" m
                        JOIN "EquityListing" l ON l."Id" = m."EquityListingId"
                        JOIN "EquitySecurity" s ON s."Id" = l."EquitySecurityId"
                        WHERE m."CommonStockId" <> s."EquityIssuerId"
                    ) THEN
                        RAISE EXCEPTION 'Retained listing symbols have conflicting issuer ownership';
                    END IF;
                END;
                $audit$;
                INSERT INTO "EquityListingTickerAlias" ("EquityListingId", "Ticker", "EvidenceSource", "RecordedAt")
                SELECT "EquityListingId", "ListedTicker", 'legacy-series-key', CURRENT_TIMESTAMP
                FROM "LegacyEquityListing"
                ON CONFLICT ("EquityListingId", "Ticker") DO NOTHING;

                CREATE FUNCTION eq_capture_listing_ticker_alias() RETURNS trigger LANGUAGE plpgsql AS $capture$
                BEGIN
                    IF NEW."Ticker" IS DISTINCT FROM OLD."Ticker" THEN
                        INSERT INTO "EquityListingTickerAlias" ("EquityListingId", "Ticker", "EvidenceSource", "RecordedAt")
                        VALUES (OLD."Id", OLD."Ticker", 'listing-symbol-change', CURRENT_TIMESTAMP)
                        ON CONFLICT ("EquityListingId", "Ticker") DO NOTHING;
                    END IF;
                    RETURN NEW;
                END;
                $capture$;
                CREATE TRIGGER equity_listing_ticker_history BEFORE UPDATE OF "Ticker" ON "EquityListing"
                    FOR EACH ROW EXECUTE FUNCTION eq_capture_listing_ticker_alias();

                CREATE FUNCTION eq_capture_legacy_listing_symbol() RETURNS trigger LANGUAGE plpgsql AS $capture$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1 FROM "EquityListing" l JOIN "EquitySecurity" s ON s."Id" = l."EquitySecurityId"
                        WHERE l."Id" = NEW."EquityListingId" AND s."EquityIssuerId" = NEW."CommonStockId"
                    ) THEN
                        RAISE EXCEPTION 'Retained listing symbol has conflicting issuer ownership';
                    END IF;
                    INSERT INTO "EquityListingTickerAlias" ("EquityListingId", "Ticker", "EvidenceSource", "RecordedAt")
                    VALUES (NEW."EquityListingId", NEW."ListedTicker", 'legacy-series-key', CURRENT_TIMESTAMP)
                    ON CONFLICT ("EquityListingId", "Ticker") DO NOTHING;
                    RETURN NEW;
                END;
                $capture$;
                CREATE TRIGGER equity_legacy_listing_symbol AFTER INSERT OR UPDATE ON "LegacyEquityListing"
                    FOR EACH ROW EXECUTE FUNCTION eq_capture_legacy_listing_symbol();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new InvalidOperationException("Rollback would discard retained listing symbols; roll forward instead.");
        }
    }
}

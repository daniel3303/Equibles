using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class BackfillLegacyEquityIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_EquityListing_Ticker",
                table: "EquityListing");

            migrationBuilder.AlterColumn<string>(
                name: "IdentitySourceUrl",
                table: "EquitySecurity",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(2000)",
                oldMaxLength: 2000);

            migrationBuilder.AlterColumn<string>(
                name: "TradingCurrency",
                table: "EquityListing",
                type: "character varying(3)",
                maxLength: 3,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(3)",
                oldMaxLength: 3);

            migrationBuilder.AlterColumn<decimal>(
                name: "QuoteUnitMultiplier",
                table: "EquityListing",
                type: "numeric(18,8)",
                precision: 18,
                scale: 8,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,8)",
                oldPrecision: 18,
                oldScale: 8);

            migrationBuilder.AlterColumn<string>(
                name: "MarketIdentifierCode",
                table: "EquityListing",
                type: "character varying(4)",
                maxLength: 4,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(4)",
                oldMaxLength: 4);

            migrationBuilder.AlterColumn<string>(
                name: "IdentitySourceUrl",
                table: "EquityListing",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(2000)",
                oldMaxLength: 2000);

            migrationBuilder.AddColumn<int>(
                name: "IdentityState",
                table: "EquityListing",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "EquityIssuer",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(256)",
                oldMaxLength: 256);

            migrationBuilder.AlterColumn<string>(
                name: "IdentitySourceUrl",
                table: "EquityIssuer",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(2000)",
                oldMaxLength: 2000);

            migrationBuilder.CreateTable(
                name: "LegacyEquityListing",
                columns: table => new
                {
                    CommonStockId = table.Column<Guid>(type: "uuid", nullable: false),
                    ListedTicker = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    EquityListingId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LegacyEquityListing", x => new { x.CommonStockId, x.ListedTicker });
                    table.ForeignKey(
                        name: "FK_LegacyEquityListing_EquityListing_EquityListingId",
                        column: x => x.EquityListingId,
                        principalTable: "EquityListing",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_EquityListing_Ticker",
                table: "EquityListing",
                sql: "\"IdentityState\" = 0 OR (length(btrim(\"Ticker\")) > 0 AND \"Ticker\" = btrim(\"Ticker\"))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_EquityListing_Verified",
                table: "EquityListing",
                sql: "\"IdentityState\" IN (0, 1) AND (\"IdentityState\" = 0 OR (\"MarketIdentifierCode\" IS NOT NULL AND \"TradingCurrency\" IS NOT NULL AND \"QuoteUnitMultiplier\" IS NOT NULL AND nullif(btrim(\"IdentitySourceUrl\"), '') IS NOT NULL))");

            migrationBuilder.CreateIndex(
                name: "IX_LegacyEquityListing_EquityListingId",
                table: "LegacyEquityListing",
                column: "EquityListingId",
                unique: true);
            migrationBuilder.Sql(LegacyIdentitySql);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("Retain migrated identities and compatibility mappings when reverting application binaries.");
        }

        private const string LegacyIdentitySql = """
-- Frozen migration: retain the original issuer/series keys as provenance.
CREATE OR REPLACE FUNCTION public.eq_ensure_legacy_listing(owner_id uuid, symbol text, protect_concurrent_writers boolean DEFAULT true)
RETURNS uuid LANGUAGE plpgsql AS $fn$
DECLARE listing_id uuid; issuer_id uuid; security_id uuid; owner_row "CommonStock"%ROWTYPE;
BEGIN
    IF owner_id IS NULL OR symbol IS NULL OR symbol = '' THEN RETURN NULL; END IF;
    SELECT "EquityListingId" INTO listing_id FROM "LegacyEquityListing"
      WHERE "CommonStockId" = owner_id AND "ListedTicker" = symbol;
    IF FOUND THEN RETURN listing_id; END IF;
    IF protect_concurrent_writers THEN
        PERFORM pg_advisory_xact_lock(hashtextextended(owner_id::text || ':' || symbol, 71023));
    END IF;
    SELECT "EquityListingId" INTO listing_id FROM "LegacyEquityListing"
      WHERE "CommonStockId" = owner_id AND "ListedTicker" = symbol;
    IF FOUND THEN RETURN listing_id; END IF;
    SELECT * INTO owner_row FROM "CommonStock" WHERE "Id" = owner_id;
    IF NOT FOUND THEN RETURN NULL; END IF;
    INSERT INTO "EquityIssuer" ("Id", "CommonStockId", "Name", "IdentitySourceUrl")
      VALUES (owner_id, owner_id, owner_row."Name", NULL)
      ON CONFLICT ("CommonStockId") DO NOTHING;
    SELECT "Id" INTO issuer_id FROM "EquityIssuer" WHERE "CommonStockId" = owner_id;
    security_id := gen_random_uuid();
    listing_id := gen_random_uuid();
    INSERT INTO "EquitySecurity" ("Id", "EquityIssuerId", "Isin", "SecurityType", "IdentitySourceUrl")
      VALUES (security_id, issuer_id, NULL, 0, NULL);
    INSERT INTO "EquityListing" ("Id", "EquitySecurityId", "MarketIdentifierCode", "Ticker",
        "TradingCurrency", "QuoteUnitMultiplier", "Active", "ListedOn", "DelistedOn", "IdentitySourceUrl", "IdentityState")
      VALUES (listing_id, security_id, NULL, symbol, NULL, NULL,
        owner_row."Active" AND COALESCE((symbol = owner_row."Ticker" OR symbol = ANY(owner_row."SecondaryTickers")
          OR symbol = ANY(owner_row."ReferenceTickers")), false), NULL, NULL, NULL, 0);
    INSERT INTO "LegacyEquityListing" ("CommonStockId", "ListedTicker", "EquityListingId")
      VALUES (owner_id, symbol, listing_id);
    RETURN listing_id;
END $fn$;

CREATE OR REPLACE FUNCTION public.eq_sync_legacy_issuer()
RETURNS trigger LANGUAGE plpgsql AS $fn$
DECLARE symbol text;
BEGIN
    INSERT INTO "EquityIssuer" ("Id", "CommonStockId", "Name", "IdentitySourceUrl")
      VALUES (NEW."Id", NEW."Id", NEW."Name", NULL)
      ON CONFLICT ("CommonStockId") DO UPDATE SET "Name" = EXCLUDED."Name"
      WHERE "EquityIssuer"."IdentitySourceUrl" IS NULL;
    FOR symbol IN SELECT DISTINCT value FROM unnest(ARRAY[NEW."Ticker"] || NEW."SecondaryTickers"
        || NEW."ReferenceTickers" || NEW."PriceHistoryBackfilledTickers") AS value WHERE value IS NOT NULL AND value <> '' ORDER BY value
    LOOP
        PERFORM public.eq_ensure_legacy_listing(NEW."Id", symbol);
    END LOOP;
    UPDATE "EquityListing" listing SET "Active" = NEW."Active" AND COALESCE(
        (listing."Ticker" = NEW."Ticker" OR listing."Ticker" = ANY(NEW."SecondaryTickers")
          OR listing."Ticker" = ANY(NEW."ReferenceTickers")), false),
      "DelistedOn" = CASE WHEN NEW."Active" AND COALESCE(
        (listing."Ticker" = NEW."Ticker" OR listing."Ticker" = ANY(NEW."SecondaryTickers")
          OR listing."Ticker" = ANY(NEW."ReferenceTickers")), false)
        THEN NULL ELSE listing."DelistedOn" END
      FROM "LegacyEquityListing" legacy
      WHERE legacy."CommonStockId" = NEW."Id" AND legacy."EquityListingId" = listing."Id"
        AND listing."IdentityState" = 0;
    RETURN NEW;
END $fn$;

CREATE TRIGGER equity_identity_issuer_write
AFTER INSERT OR UPDATE OF "Ticker", "Name", "Active", "SecondaryTickers", "ReferenceTickers", "PriceHistoryBackfilledTickers"
ON "CommonStock" FOR EACH ROW EXECUTE FUNCTION public.eq_sync_legacy_issuer();

-- One generic guard covers SQL, bulk/upsert and retiring binaries as well as EF writers.
CREATE OR REPLACE FUNCTION public.eq_sync_legacy_series()
RETURNS trigger LANGUAGE plpgsql AS $fn$
BEGIN
    PERFORM public.eq_ensure_legacy_listing((to_jsonb(NEW)->>'CommonStockId')::uuid,
      to_jsonb(NEW)->>TG_ARGV[0]);
    RETURN NEW;
END $fn$;

INSERT INTO "EquityIssuer" ("Id", "CommonStockId", "Name", "IdentitySourceUrl")
SELECT "Id", "Id", "Name", NULL FROM "CommonStock"
ON CONFLICT ("CommonStockId") DO NOTHING;

-- These new tables and guards are not visible outside this migration transaction.
-- Avoid exhausting PostgreSQL advisory-lock slots across the historical universe.
SELECT public.eq_ensure_legacy_listing(stock."Id", symbol, false)
FROM "CommonStock" stock
CROSS JOIN LATERAL (SELECT DISTINCT value AS symbol FROM unnest(ARRAY[stock."Ticker"]
  || stock."SecondaryTickers" || stock."ReferenceTickers" || stock."PriceHistoryBackfilledTickers") value WHERE value IS NOT NULL AND value <> '') symbols;

-- Module tables are optional in self-hosted installations. Each has its own frozen source column.
DO $body$
DECLARE source_table text; symbol_column text;
BEGIN
    FOR source_table, symbol_column IN SELECT * FROM (VALUES
      ('ListedDailyStockPrice', 'ListedTicker'),
      ('CommonStockDelistedListing', 'ListedTicker'),
      ('CommonStockTickerAlias', 'Ticker'),
      ('CommonStockTickerEvidence', 'Ticker'),
      ('CommonStockListedCusip', 'ListedTicker'),
      ('StockSplit', 'PriceSeriesTicker'),
      ('InstitutionalHolding', 'ListedTicker'),
      ('StockQuarterlyListingActivity', 'PriceSeriesTicker'),
      ('DailyShortVolume', 'ListedTicker'),
      ('ShortInterest', 'ListedTicker'),
      ('OffExchangeVolume', 'ListedTicker'),
      ('FailToDeliver', 'ListedTicker')
    ) sources(table_name, column_name)
    LOOP
      IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = 'public'
          AND table_name = source_table AND column_name = symbol_column) THEN
        EXECUTE format('CREATE TRIGGER equity_identity_series_write AFTER INSERT OR UPDATE OF "CommonStockId", %I ON %I FOR EACH ROW EXECUTE FUNCTION public.eq_sync_legacy_series(%L)',
          symbol_column, source_table, symbol_column);
        EXECUTE format('SELECT public.eq_ensure_legacy_listing(series."CommonStockId", series.symbol, false)
          FROM (SELECT DISTINCT "CommonStockId", %I AS symbol FROM %I WHERE nullif(%I, '''') IS NOT NULL) series
          JOIN "CommonStock" stock ON stock."Id" = series."CommonStockId"',
          symbol_column, source_table, symbol_column);
      END IF;
    END LOOP;
END $body$;
""";
    }
}

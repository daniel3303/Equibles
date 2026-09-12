using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    public partial class RetargetFinraObservationsToListings : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE FUNCTION public.eq_bridge_finra_listing()
                RETURNS trigger LANGUAGE plpgsql AS $fn$
                DECLARE issuer_id uuid;
                BEGIN
                    IF NEW."EquityListingId" IS NULL OR NEW."EquityListingId" = '00000000-0000-0000-0000-000000000000'::uuid
                        OR (TG_OP = 'UPDATE' AND NEW."CommonStockId" IS NOT NULL AND NEW."EquityListingId" IS NOT DISTINCT FROM OLD."EquityListingId"
                            AND (NEW."CommonStockId" IS DISTINCT FROM OLD."CommonStockId"
                                OR NEW."ListedTicker" IS DISTINCT FROM OLD."ListedTicker")) THEN
                        NEW."EquityListingId" := public.eq_ensure_legacy_listing(NEW."CommonStockId", NEW."ListedTicker");
                        IF NEW."EquityListingId" IS NULL THEN
                            RAISE EXCEPTION 'FINRA observation requires an exact listing identity';
                        END IF;
                    END IF;
                    SELECT security."EquityIssuerId" INTO STRICT issuer_id
                    FROM "EquityListing" listing JOIN "EquitySecurity" security ON security."Id" = listing."EquitySecurityId"
                    WHERE listing."Id" = NEW."EquityListingId";
                    IF NEW."CommonStockId" IS NOT NULL AND NEW."CommonStockId" <> issuer_id THEN
                        RAISE EXCEPTION 'FINRA listing and original issuer disagree';
                    END IF;
                    -- Native-only venues have no legacy identity. NULL keeps the retiring
                    -- issuer/ticker unique key from merging equal symbols across venues.
                    SELECT mapping."CommonStockId" INTO NEW."CommonStockId"
                    FROM "LegacyEquityListing" mapping WHERE mapping."EquityListingId" = NEW."EquityListingId";
                    RETURN NEW;
                END $fn$;
                """);
            migrationBuilder.Sql("""
                ALTER TABLE "DailyShortVolume" ADD COLUMN "EquityListingId" uuid;
                UPDATE "DailyShortVolume" observation
                SET "EquityListingId" = mapping."EquityListingId"
                FROM "LegacyEquityListing" mapping
                WHERE mapping."CommonStockId" = observation."CommonStockId"
                    AND mapping."ListedTicker" = observation."ListedTicker";
                DO $body$
                BEGIN
                    IF EXISTS (SELECT 1 FROM "DailyShortVolume" WHERE "EquityListingId" IS NULL) THEN
                        RAISE EXCEPTION 'DailyShortVolume history has unresolved listing identities; no observations were changed.';
                    END IF;
                END $body$;
                ALTER TABLE "DailyShortVolume" ALTER COLUMN "EquityListingId" SET NOT NULL;
                ALTER TABLE "DailyShortVolume" ALTER COLUMN "CommonStockId" DROP NOT NULL;

                -- Retain the old physical column/index only through the retiring-binary window.
                -- The native model and writer use ListingId; final cutover removes this guard.
                DROP TRIGGER IF EXISTS equity_identity_series_write ON "DailyShortVolume";
                CREATE TRIGGER equity_finra_listing_bridge BEFORE INSERT OR UPDATE ON "DailyShortVolume"
                    FOR EACH ROW EXECUTE FUNCTION public.eq_bridge_finra_listing();
                """);
            migrationBuilder.DropForeignKey(
                name: "FK_DailyShortVolume_CommonStock_CommonStockId", table: "DailyShortVolume");
            migrationBuilder.CreateIndex(
                name: "IX_DailyShortVolume_EquityListingId_Date", table: "DailyShortVolume",
                columns: new[] { "EquityListingId", "Date" }, unique: true);
            migrationBuilder.AddForeignKey(
                name: "FK_DailyShortVolume_EquityListing_EquityListingId", table: "DailyShortVolume",
                column: "EquityListingId", principalTable: "EquityListing", principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
            migrationBuilder.Sql("""
                ALTER TABLE "ShortInterest" ADD COLUMN "EquityListingId" uuid;
                UPDATE "ShortInterest" observation
                SET "EquityListingId" = mapping."EquityListingId"
                FROM "LegacyEquityListing" mapping
                WHERE mapping."CommonStockId" = observation."CommonStockId"
                    AND mapping."ListedTicker" = observation."ListedTicker";
                DO $body$
                BEGIN
                    IF EXISTS (SELECT 1 FROM "ShortInterest" WHERE "EquityListingId" IS NULL) THEN
                        RAISE EXCEPTION 'ShortInterest history has unresolved listing identities; no observations were changed.';
                    END IF;
                END $body$;
                ALTER TABLE "ShortInterest" ALTER COLUMN "EquityListingId" SET NOT NULL;
                ALTER TABLE "ShortInterest" ALTER COLUMN "CommonStockId" DROP NOT NULL;

                -- Retain the old physical column/index only through the retiring-binary window.
                -- The native model and writer use ListingId; final cutover removes this guard.
                DROP TRIGGER IF EXISTS equity_identity_series_write ON "ShortInterest";
                CREATE TRIGGER equity_finra_listing_bridge BEFORE INSERT OR UPDATE ON "ShortInterest"
                    FOR EACH ROW EXECUTE FUNCTION public.eq_bridge_finra_listing();
                """);
            migrationBuilder.DropForeignKey(
                name: "FK_ShortInterest_CommonStock_CommonStockId", table: "ShortInterest");
            migrationBuilder.CreateIndex(
                name: "IX_ShortInterest_EquityListingId_SettlementDate", table: "ShortInterest",
                columns: new[] { "EquityListingId", "SettlementDate" }, unique: true);
            migrationBuilder.AddForeignKey(
                name: "FK_ShortInterest_EquityListing_EquityListingId", table: "ShortInterest",
                column: "EquityListingId", principalTable: "EquityListing", principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
            migrationBuilder.Sql("""
                ALTER TABLE "OffExchangeVolume" ADD COLUMN "EquityListingId" uuid;
                UPDATE "OffExchangeVolume" observation
                SET "EquityListingId" = mapping."EquityListingId"
                FROM "LegacyEquityListing" mapping
                WHERE mapping."CommonStockId" = observation."CommonStockId"
                    AND mapping."ListedTicker" = observation."ListedTicker";
                DO $body$
                BEGIN
                    IF EXISTS (SELECT 1 FROM "OffExchangeVolume" WHERE "EquityListingId" IS NULL) THEN
                        RAISE EXCEPTION 'OffExchangeVolume history has unresolved listing identities; no observations were changed.';
                    END IF;
                END $body$;
                ALTER TABLE "OffExchangeVolume" ALTER COLUMN "EquityListingId" SET NOT NULL;
                ALTER TABLE "OffExchangeVolume" ALTER COLUMN "CommonStockId" DROP NOT NULL;

                -- Retain the old physical column/index only through the retiring-binary window.
                -- The native model and writer use ListingId; final cutover removes this guard.
                DROP TRIGGER IF EXISTS equity_identity_series_write ON "OffExchangeVolume";
                CREATE TRIGGER equity_finra_listing_bridge BEFORE INSERT OR UPDATE ON "OffExchangeVolume"
                    FOR EACH ROW EXECUTE FUNCTION public.eq_bridge_finra_listing();
                """);
            migrationBuilder.DropForeignKey(
                name: "FK_OffExchangeVolume_CommonStock_CommonStockId", table: "OffExchangeVolume");
            migrationBuilder.CreateIndex(
                name: "IX_OffExchangeVolume_EquityListingId_WeekStartDate", table: "OffExchangeVolume",
                columns: new[] { "EquityListingId", "WeekStartDate" }, unique: true);
            migrationBuilder.AddForeignKey(
                name: "FK_OffExchangeVolume_EquityListing_EquityListingId", table: "OffExchangeVolume",
                column: "EquityListingId", principalTable: "EquityListing", principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        protected override void Down(MigrationBuilder migrationBuilder) =>
            throw new NotSupportedException("Retain listing identities and all FINRA observations when reverting application binaries.");
    }
}

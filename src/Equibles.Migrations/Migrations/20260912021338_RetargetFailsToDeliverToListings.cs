using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    public partial class RetargetFailsToDeliverToListings : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE "FailToDeliver" ADD COLUMN "EquityListingId" uuid;
                UPDATE "FailToDeliver" observation
                SET "EquityListingId" = mapping."EquityListingId"
                FROM "LegacyEquityListing" mapping
                WHERE mapping."CommonStockId" = observation."CommonStockId"
                    AND mapping."ListedTicker" = observation."ListedTicker";
                DO $body$
                BEGIN
                    IF EXISTS (SELECT 1 FROM "FailToDeliver" WHERE "EquityListingId" IS NULL) THEN
                        RAISE EXCEPTION 'Fails-to-deliver history has unresolved listing identities; no observations were changed.';
                    END IF;
                END $body$;
                ALTER TABLE "FailToDeliver" ALTER COLUMN "EquityListingId" SET NOT NULL;
                ALTER TABLE "FailToDeliver" ALTER COLUMN "CommonStockId" DROP NOT NULL;

                -- Retain the old physical column/index only through the retiring-binary window.
                -- The native model and writer use ListingId; final cutover removes this guard.
                DROP TRIGGER IF EXISTS equity_identity_series_write ON "FailToDeliver";
                CREATE FUNCTION public.eq_bridge_ftd_listing()
                RETURNS trigger LANGUAGE plpgsql AS $fn$
                DECLARE issuer_id uuid;
                BEGIN
                    IF NEW."EquityListingId" IS NULL OR NEW."EquityListingId" = '00000000-0000-0000-0000-000000000000'::uuid
                        OR (TG_OP = 'UPDATE' AND NEW."CommonStockId" IS NOT NULL AND NEW."EquityListingId" IS NOT DISTINCT FROM OLD."EquityListingId"
                            AND (NEW."CommonStockId" IS DISTINCT FROM OLD."CommonStockId"
                                OR NEW."ListedTicker" IS DISTINCT FROM OLD."ListedTicker")) THEN
                        NEW."EquityListingId" := public.eq_ensure_legacy_listing(NEW."CommonStockId", NEW."ListedTicker");
                        IF NEW."EquityListingId" IS NULL THEN
                            RAISE EXCEPTION 'Fails-to-deliver observation requires an exact listing identity';
                        END IF;
                    END IF;
                    SELECT security."EquityIssuerId" INTO STRICT issuer_id
                    FROM "EquityListing" listing JOIN "EquitySecurity" security ON security."Id" = listing."EquitySecurityId"
                    WHERE listing."Id" = NEW."EquityListingId";
                    IF NEW."CommonStockId" IS NOT NULL AND NEW."CommonStockId" <> issuer_id THEN
                        RAISE EXCEPTION 'Fails-to-deliver listing and original issuer disagree';
                    END IF;
                    -- Native-only venues have no legacy identity. NULL keeps the retiring
                    -- issuer/ticker unique key from merging equal symbols across venues.
                    SELECT mapping."CommonStockId" INTO NEW."CommonStockId"
                    FROM "LegacyEquityListing" mapping WHERE mapping."EquityListingId" = NEW."EquityListingId";
                    RETURN NEW;
                END $fn$;
                CREATE TRIGGER equity_ftd_listing_bridge BEFORE INSERT OR UPDATE ON "FailToDeliver"
                    FOR EACH ROW EXECUTE FUNCTION public.eq_bridge_ftd_listing();
                """);
            migrationBuilder.DropForeignKey(
                name: "FK_FailToDeliver_CommonStock_CommonStockId", table: "FailToDeliver");
            migrationBuilder.CreateIndex(
                name: "IX_FailToDeliver_EquityListingId_SettlementDate", table: "FailToDeliver",
                columns: new[] { "EquityListingId", "SettlementDate" }, unique: true);
            migrationBuilder.AddForeignKey(
                name: "FK_FailToDeliver_EquityListing_EquityListingId", table: "FailToDeliver",
                column: "EquityListingId", principalTable: "EquityListing", principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        protected override void Down(MigrationBuilder migrationBuilder) =>
            throw new NotSupportedException("Retain listing identities and all observation history when reverting application binaries.");
    }
}

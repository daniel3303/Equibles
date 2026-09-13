using Equibles.Migrations.Infrastructure;
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
                CREATE OR REPLACE FUNCTION public.eq_bridge_finra_listing()
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
            NativeListingObservationExpansion20260912.Apply(migrationBuilder, "DailyShortVolume", "Date", "finra");
            NativeListingObservationExpansion20260912.Apply(migrationBuilder, "ShortInterest", "SettlementDate", "finra");
            NativeListingObservationExpansion20260912.Apply(migrationBuilder, "OffExchangeVolume", "WeekStartDate", "finra");
        }

        protected override void Down(MigrationBuilder migrationBuilder) =>
            throw new NotSupportedException("Retain listing identities and all FINRA observations when reverting application binaries.");
    }
}

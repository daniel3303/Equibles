using Microsoft.EntityFrameworkCore.Migrations;

namespace Equibles.Migrations.Migrations;

public partial class UseNativeCorporateActionGuards : Migration {
    protected override void Up(MigrationBuilder migrationBuilder) {
        migrationBuilder.Sql("""
            SET LOCAL lock_timeout = '5s';

            CREATE OR REPLACE FUNCTION public."InvalidateRevisedStockSplitAdjustment"()
             RETURNS trigger
             LANGUAGE plpgsql
            AS $function$
            BEGIN
                IF ROW(
                    OLD."EquityIssuerId",
                    OLD."PriceSeriesTicker",
                    OLD."EffectiveDate",
                    OLD."Numerator",
                    OLD."Denominator",
                    OLD."Source"
                ) IS DISTINCT FROM ROW(
                    NEW."EquityIssuerId",
                    NEW."PriceSeriesTicker",
                    NEW."EffectiveDate",
                    NEW."Numerator",
                    NEW."Denominator",
                    NEW."Source"
                ) THEN
                    NEW."PriceAdjustmentAppliedTime" := NULL;
                END IF;
                RETURN NEW;
            END;
            $function$;

            CREATE OR REPLACE FUNCTION public.eq_validate_corporate_action_listing()
             RETURNS trigger
             LANGUAGE plpgsql
            AS $function$
            DECLARE owner_id uuid; source_ticker text; denomination text;
            BEGIN
                IF TG_OP = 'UPDATE' AND OLD."EquityListingId" IS NOT NULL AND NEW."EquityListingId" IS DISTINCT FROM OLD."EquityListingId" THEN
                    RAISE EXCEPTION 'A recorded corporate action listing cannot be changed';
                END IF;
                IF NEW."EquityListingId" IS NULL THEN RETURN NEW; END IF;
                SELECT s."EquityIssuerId", l."Ticker", l."TradingCurrency"
                INTO owner_id, source_ticker, denomination
                FROM "EquityListing" l JOIN "EquitySecurity" s ON s."Id" = l."EquitySecurityId"
                WHERE l."Id" = NEW."EquityListingId" FOR SHARE OF l, s;
                IF owner_id IS DISTINCT FROM NEW."EquityIssuerId" THEN
                    RAISE EXCEPTION 'Corporate action listing belongs to another issuer';
                END IF;
                IF TG_TABLE_NAME = 'StockSplit' THEN
                    IF NEW."PriceSeriesTicker" IS NULL OR (NEW."PriceSeriesTicker" <> source_ticker AND NOT EXISTS (
                        SELECT 1 FROM "EquityListingTickerAlias" a
                        WHERE a."EquityListingId" = NEW."EquityListingId" AND a."Ticker" = NEW."PriceSeriesTicker"
                    )) THEN RAISE EXCEPTION 'Split source symbol does not identify its recorded listing'; END IF;
                ELSE
                    IF NEW."Currency" IS NULL OR NEW."Currency" IS DISTINCT FROM denomination THEN
                        RAISE EXCEPTION 'Dividend denomination does not match its recorded listing';
                    END IF;
                END IF;
                RETURN NEW;
            END;
            $function$;

            -- Watch every UPDATE: an old writer targets CommonStockId and the earlier mirror
            -- assigns EquityIssuerId before this guard runs. UPDATE OF ignores trigger assignments.
            DROP TRIGGER "TR_StockSplit_InvalidateRevisedAdjustment" ON "StockSplit";
            CREATE TRIGGER "TR_StockSplit_InvalidateRevisedAdjustment"
            BEFORE UPDATE ON "StockSplit" FOR EACH ROW
            EXECUTE FUNCTION "InvalidateRevisedStockSplitAdjustment"();
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Native ownership guards must remain installed; roll forward instead.");
}

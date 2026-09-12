using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class PreserveRenamedAndRetiredPriceSeries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION eq_sync_native_daily_price() RETURNS trigger LANGUAGE plpgsql AS $body$
                DECLARE
                    owner_id uuid;
                    listing_id uuid;
                BEGIN
                    IF current_setting('equibles.native_price_mirror', true) = 'on' THEN
                        IF TG_OP = 'DELETE' THEN RETURN OLD; END IF;
                        RETURN NEW;
                    END IF;
                    IF TG_TABLE_NAME = 'DailyStockPrice' THEN
                        IF TG_OP = 'DELETE' THEN
                            -- A retiring directory cascade cannot erase the native archive.
                            IF NOT EXISTS (SELECT 1 FROM "CommonStock" WHERE "Id" = OLD."CommonStockId") THEN
                                RETURN OLD;
                            END IF;
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
                            -- A retiring directory cascade cannot erase the native archive.
                            IF NOT EXISTS (SELECT 1 FROM "CommonStock" WHERE "Id" = OLD."CommonStockId") THEN
                                RETURN OLD;
                            END IF;
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

                -- Only the registered legacy series are visible to retiring U.S. readers.
                -- The transaction-local flag suppresses the reverse hop, not unrelated nested triggers.
                CREATE OR REPLACE FUNCTION eq_sync_retiring_daily_price() RETURNS trigger LANGUAGE plpgsql AS $body$
                DECLARE
                    mapping "LegacyEquityListing"%ROWTYPE;
                    previous_flag text;
                BEGIN
                    IF current_setting('equibles.native_price_mirror', true) = 'on' THEN
                        IF TG_OP = 'DELETE' THEN RETURN OLD; END IF;
                        RETURN NEW;
                    END IF;
                    previous_flag := current_setting('equibles.native_price_mirror', true);
                    PERFORM set_config('equibles.native_price_mirror', 'on', true);
                    IF TG_OP = 'DELETE' THEN
                        DELETE FROM "ListedDailyStockPrice" WHERE "Id" = OLD."Id";
                        PERFORM set_config('equibles.native_price_mirror', COALESCE(previous_flag, 'off'), true);
                        RETURN OLD;
                    END IF;
                    SELECT m.* INTO mapping FROM "LegacyEquityListing" m
                    JOIN "CommonStock" s ON s."Id" = m."CommonStockId"
                    WHERE m."EquityListingId" = NEW."EquityListingId";
                    IF FOUND THEN
                        IF NEW."SourceTicker" IS DISTINCT FROM mapping."ListedTicker"
                            AND NOT EXISTS (
                                SELECT 1 FROM "EquityListing" listing
                                WHERE listing."Id" = NEW."EquityListingId" AND listing."Ticker" = NEW."SourceTicker"
                            ) AND NOT EXISTS (
                                SELECT 1 FROM "EquityListingTickerAlias" alias
                                WHERE alias."EquityListingId" = NEW."EquityListingId" AND alias."Ticker" = NEW."SourceTicker"
                            ) THEN
                            RAISE EXCEPTION 'Native daily bar % conflicts with its registered source ticker', NEW."Id";
                        END IF;
                        INSERT INTO "ListedDailyStockPrice"
                            ("Id", "CommonStockId", "ListedTicker", "Date", "Open", "High", "Low", "Close", "AdjustedClose", "Volume", "CreationTime")
                        VALUES (NEW."Id", mapping."CommonStockId", mapping."ListedTicker", NEW."Date", NEW."Open", NEW."High", NEW."Low", NEW."Close", NEW."AdjustedClose", NEW."Volume", NEW."CreationTime")
                        ON CONFLICT ("Id") DO UPDATE SET
                            "CommonStockId" = EXCLUDED."CommonStockId", "ListedTicker" = EXCLUDED."ListedTicker", "Date" = EXCLUDED."Date",
                            "Open" = EXCLUDED."Open", "High" = EXCLUDED."High", "Low" = EXCLUDED."Low", "Close" = EXCLUDED."Close",
                            "AdjustedClose" = EXCLUDED."AdjustedClose", "Volume" = EXCLUDED."Volume", "CreationTime" = EXCLUDED."CreationTime"
                        WHERE ROW("ListedDailyStockPrice"."CommonStockId", "ListedDailyStockPrice"."ListedTicker", "ListedDailyStockPrice"."Date",
                            "ListedDailyStockPrice"."Open", "ListedDailyStockPrice"."High", "ListedDailyStockPrice"."Low", "ListedDailyStockPrice"."Close",
                            "ListedDailyStockPrice"."AdjustedClose", "ListedDailyStockPrice"."Volume", "ListedDailyStockPrice"."CreationTime")
                        IS DISTINCT FROM ROW(EXCLUDED."CommonStockId", EXCLUDED."ListedTicker", EXCLUDED."Date", EXCLUDED."Open", EXCLUDED."High",
                            EXCLUDED."Low", EXCLUDED."Close", EXCLUDED."AdjustedClose", EXCLUDED."Volume", EXCLUDED."CreationTime");
                    ELSIF TG_OP = 'UPDATE' THEN
                        DELETE FROM "ListedDailyStockPrice" WHERE "Id" = OLD."Id";
                    END IF;
                    PERFORM set_config('equibles.native_price_mirror', COALESCE(previous_flag, 'off'), true);
                    RETURN NEW;
                END;
                $body$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new System.NotSupportedException("Price-source identity and retained-history guards cannot be removed by rollback.");
        }
    }
}

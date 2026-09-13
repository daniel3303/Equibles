using Microsoft.EntityFrameworkCore.Migrations;

namespace Equibles.Migrations.Infrastructure;

// Frozen with the initial native price migration; source rows and checkpoints commit together.
internal static class NativeEquityPriceExpansion20260912
{
    public static void Apply(MigrationBuilder migration)
    {
        migration.Sql(SchemaAndBridges, suppressTransaction: true);
        foreach (var source in new[] { "DailyStockPrice", "ListedDailyStockPrice" })
        {
            migration.Sql(
                $"""ALTER TABLE "{source}" VALIDATE CONSTRAINT "FK_{source}_EquityIssuer_CommonStockId";""",
                suppressTransaction: true
            );
            migration.Sql(Backfill(source), suppressTransaction: true);
        }
    }

    private static string Backfill(string source)
    {
        var exact = source == "ListedDailyStockPrice";
        var target = exact ? "EquityDailyStockPrice" : "UnattributedDailyStockPrice";
        var identityColumn = exact ? "EquityListingId" : "EquityIssuerId";
        var sourceIdentity = exact ? "mapping.\"EquityListingId\"" : "mapping.\"Id\"";
        var mappingJoin = exact
            ? "LEFT JOIN \"LegacyEquityListing\" mapping ON mapping.\"CommonStockId\" = original.\"CommonStockId\" AND mapping.\"ListedTicker\" = original.\"ListedTicker\""
            : "LEFT JOIN \"EquityIssuer\" mapping ON mapping.\"Id\" = original.\"CommonStockId\"";
        string[] fields =
        [
            "Id",
            "Date",
            "Open",
            "High",
            "Low",
            "Close",
            "AdjustedClose",
            "Volume",
            "CreationTime",
        ];
        var originalValues =
            string.Join(", ", fields.Select(field => $"original.\"{field}\""))
            + $", {sourceIdentity}"
            + (exact ? ", original.\"ListedTicker\"" : "");
        var targetValues =
            string.Join(", ", fields.Select(field => $"native.\"{field}\""))
            + $", native.\"{identityColumn}\""
            + (exact ? ", native.\"SourceTicker\"" : "");
        var targetColumns =
            string.Join(", ", fields.Select(field => $"\"{field}\""))
            + $", \"{identityColumn}\""
            + (exact ? ", \"SourceTicker\"" : "");
        return $"""
            DO $copy$
            DECLARE previous_id uuid; batch_ids uuid[]; copied bigint; completed boolean;
            BEGIN
                SELECT "AfterId", "Completed" INTO previous_id, completed
                    FROM "NativePriceMigrationProgress" WHERE "TableName" = '{source}';
                IF completed THEN RETURN; END IF;
                LOOP
                    IF previous_id IS NULL THEN
                        SELECT array_agg("Id" ORDER BY "Id") INTO batch_ids
                            FROM (SELECT "Id" FROM "{source}" ORDER BY "Id" LIMIT 10000) batch;
                    ELSE
                        SELECT array_agg("Id" ORDER BY "Id") INTO batch_ids
                            FROM (SELECT "Id" FROM "{source}" WHERE "Id" > previous_id ORDER BY "Id" LIMIT 10000) batch;
                    END IF;
                    IF batch_ids IS NULL THEN
                        UPDATE "NativePriceMigrationProgress" SET "Completed" = true WHERE "TableName" = '{source}';
                        COMMIT;
                        RETURN;
                    END IF;
                    -- Serialize with source corrections/deletes before reading values to copy.
                    PERFORM 1 FROM "{source}" WHERE "Id" = ANY(batch_ids) ORDER BY "Id" FOR SHARE;
                    IF EXISTS (SELECT 1 FROM "{source}" original {mappingJoin}
                        WHERE original."Id" = ANY(batch_ids) AND {sourceIdentity} IS NULL) THEN
                        RAISE EXCEPTION '{source} has unresolved native price identities; the incomplete batch was not copied';
                    END IF;
                    INSERT INTO "{target}" ({targetColumns})
                    SELECT {originalValues} FROM "{source}" original {mappingJoin}
                        WHERE original."Id" = ANY(batch_ids)
                    ON CONFLICT ("Id") DO NOTHING;
                    GET DIAGNOSTICS copied = ROW_COUNT;
                    IF EXISTS (SELECT 1 FROM "{source}" original {mappingJoin}
                        LEFT JOIN "{target}" native ON native."Id" = original."Id"
                        WHERE original."Id" = ANY(batch_ids)
                            AND ROW({originalValues}) IS DISTINCT FROM ROW({targetValues})) THEN
                        RAISE EXCEPTION '{source} original fields differ from native prices; the incomplete batch was not copied';
                    END IF;
                    UPDATE "NativePriceMigrationProgress" SET "AfterId" = batch_ids[array_length(batch_ids, 1)],
                        "CopiedRows" = "CopiedRows" + copied WHERE "TableName" = '{source}';
                    COMMIT;
                    previous_id := batch_ids[array_length(batch_ids, 1)];
                END LOOP;
            END $copy$;
            """;
    }

    private const string SchemaAndBridges = """
        DO $setup$
        BEGIN
            SET LOCAL lock_timeout = '5s';
            IF EXISTS (SELECT attname FROM pg_attribute WHERE attrelid = '"DailyStockPrice"'::regclass
                AND attnum > 0 AND NOT attisdropped AND attname <> ALL(ARRAY['Id', 'Date', 'Open', 'High', 'Low', 'Close', 'AdjustedClose', 'Volume', 'CreationTime', 'CommonStockId'])) THEN
                RAISE EXCEPTION 'DailyStockPrice has unexpected original fields; preserve their mapping before copying prices';
            END IF;
            IF EXISTS (SELECT attname FROM pg_attribute WHERE attrelid = '"ListedDailyStockPrice"'::regclass
                AND attnum > 0 AND NOT attisdropped AND attname <> ALL(ARRAY['Id', 'Date', 'Open', 'High', 'Low', 'Close', 'AdjustedClose', 'Volume', 'CreationTime', 'CommonStockId', 'ListedTicker'])) THEN
                RAISE EXCEPTION 'ListedDailyStockPrice has unexpected original fields; preserve their mapping before copying prices';
            END IF;
            CREATE TABLE IF NOT EXISTS "EquityDailyStockPrice" (
                "Id" uuid NOT NULL,
                "EquityListingId" uuid NOT NULL,
                "SourceTicker" character varying(32),
                "Date" date NOT NULL,
                "Open" numeric(18,4) NOT NULL,
                "High" numeric(18,4) NOT NULL,
                "Low" numeric(18,4) NOT NULL,
                "Close" numeric(18,4) NOT NULL,
                "AdjustedClose" numeric(18,4) NOT NULL,
                "Volume" bigint NOT NULL,
                "CreationTime" timestamp with time zone NOT NULL,
                CONSTRAINT "PK_EquityDailyStockPrice" PRIMARY KEY ("Id"),
                CONSTRAINT "FK_EquityDailyStockPrice_EquityListing_EquityListingId" FOREIGN KEY ("EquityListingId") REFERENCES "EquityListing" ("Id") ON DELETE RESTRICT
            );
            
            CREATE TABLE IF NOT EXISTS "UnattributedDailyStockPrice" (
                "Id" uuid NOT NULL,
                "EquityIssuerId" uuid NOT NULL,
                "Date" date NOT NULL,
                "Open" numeric(18,4) NOT NULL,
                "High" numeric(18,4) NOT NULL,
                "Low" numeric(18,4) NOT NULL,
                "Close" numeric(18,4) NOT NULL,
                "AdjustedClose" numeric(18,4) NOT NULL,
                "Volume" bigint NOT NULL,
                "CreationTime" timestamp with time zone NOT NULL,
                CONSTRAINT "PK_UnattributedDailyStockPrice" PRIMARY KEY ("Id"),
                CONSTRAINT "FK_UnattributedDailyStockPrice_EquityIssuer_EquityIssuerId" FOREIGN KEY ("EquityIssuerId") REFERENCES "EquityIssuer" ("Id") ON DELETE RESTRICT
            );
            
            CREATE INDEX IF NOT EXISTS "IX_EquityDailyStockPrice_Date" ON "EquityDailyStockPrice" ("Date");
            
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_EquityDailyStockPrice_EquityListingId_Date" ON "EquityDailyStockPrice" ("EquityListingId", "Date");
            
            CREATE INDEX IF NOT EXISTS "IX_UnattributedDailyStockPrice_Date" ON "UnattributedDailyStockPrice" ("Date");
            
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_UnattributedDailyStockPrice_EquityIssuerId_Date" ON "UnattributedDailyStockPrice" ("EquityIssuerId", "Date");
            
            CREATE TABLE IF NOT EXISTS "NativePriceMigrationProgress" (
                "TableName" text PRIMARY KEY, "AfterId" uuid,
                "Completed" boolean NOT NULL DEFAULT false,
                "CopiedRows" bigint NOT NULL DEFAULT 0);
            ALTER TABLE "DailyStockPrice" DROP CONSTRAINT IF EXISTS "FK_DailyStockPrice_CommonStock_CommonStockId";
            IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conrelid = '"DailyStockPrice"'::regclass
                AND conname = 'FK_DailyStockPrice_EquityIssuer_CommonStockId') THEN
                ALTER TABLE "DailyStockPrice" ADD CONSTRAINT "FK_DailyStockPrice_EquityIssuer_CommonStockId"
                    FOREIGN KEY ("CommonStockId") REFERENCES "EquityIssuer"("Id") ON DELETE RESTRICT NOT VALID;
            END IF;
            ALTER TABLE "ListedDailyStockPrice" DROP CONSTRAINT IF EXISTS "FK_ListedDailyStockPrice_CommonStock_CommonStockId";
            IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conrelid = '"ListedDailyStockPrice"'::regclass
                AND conname = 'FK_ListedDailyStockPrice_EquityIssuer_CommonStockId') THEN
                ALTER TABLE "ListedDailyStockPrice" ADD CONSTRAINT "FK_ListedDailyStockPrice_EquityIssuer_CommonStockId"
                    FOREIGN KEY ("CommonStockId") REFERENCES "EquityIssuer"("Id") ON DELETE RESTRICT NOT VALID;
            END IF;
            -- Temporary writers keep old binaries consistent until every consumer has moved.
            -- Creating these triggers first blocks source writes until this transaction commits.
            CREATE OR REPLACE FUNCTION eq_sync_native_daily_price() RETURNS trigger LANGUAGE plpgsql AS $body$
            DECLARE
                owner_id uuid;
                listing_id uuid;
            BEGIN
                IF TG_TABLE_NAME = 'DailyStockPrice' THEN
                    IF TG_OP = 'DELETE' THEN
                        DELETE FROM "UnattributedDailyStockPrice" WHERE "Id" = OLD."Id";
                        RETURN OLD;
                    END IF;
                    SELECT "Id" INTO STRICT owner_id FROM "EquityIssuer" WHERE "Id" = NEW."CommonStockId";
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
            CREATE OR REPLACE TRIGGER equity_native_daily_price_write AFTER INSERT OR UPDATE OR DELETE ON "ListedDailyStockPrice"
            FOR EACH ROW EXECUTE FUNCTION eq_sync_native_daily_price();
            CREATE OR REPLACE TRIGGER equity_native_unattributed_price_write AFTER INSERT OR UPDATE OR DELETE ON "DailyStockPrice"
            FOR EACH ROW EXECUTE FUNCTION eq_sync_native_daily_price();
            
            
            INSERT INTO "NativePriceMigrationProgress" ("TableName") VALUES ('DailyStockPrice'), ('ListedDailyStockPrice')
                ON CONFLICT ("TableName") DO NOTHING;
        END $setup$;
        """;
}

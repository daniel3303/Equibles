using Microsoft.EntityFrameworkCore.Migrations;

namespace Equibles.Migrations.Infrastructure;

// Frozen with the 2026-09-12 migrations so interrupted historical upgrades replay identically.
internal static class NativeListingObservationExpansion20260912
{
    public static void Apply(
        MigrationBuilder migration,
        string table,
        string dateColumn,
        string bridge
    )
    {
        var foreignKey = $"FK_{table}_EquityListing_EquityListingId";
        var index = $"IX_{table}_EquityListingId_{dateColumn}";
        var required = $"CK_{table}_NativeListingRequired";
        migration.Sql(
            $"""
            DO $setup$
            BEGIN
                SET LOCAL lock_timeout = '5s';
                CREATE TABLE IF NOT EXISTS "NativeListingObservationMigrationProgress" (
                    "TableName" text PRIMARY KEY, "AfterId" uuid,
                    "Completed" boolean NOT NULL DEFAULT false,
                    "UpdatedRows" bigint NOT NULL DEFAULT 0);
                ALTER TABLE "{table}" ADD COLUMN IF NOT EXISTS "EquityListingId" uuid;
                ALTER TABLE "{table}" ALTER COLUMN "CommonStockId" DROP NOT NULL;
                DROP TRIGGER IF EXISTS equity_identity_series_write ON "{table}";
                CREATE OR REPLACE TRIGGER equity_{bridge}_listing_bridge BEFORE INSERT OR UPDATE ON "{table}"
                    FOR EACH ROW EXECUTE FUNCTION public.eq_bridge_{bridge}_listing();
                ALTER TABLE "{table}" DROP CONSTRAINT IF EXISTS "FK_{table}_CommonStock_CommonStockId";
                IF NOT EXISTS (SELECT 1 FROM pg_constraint
                    WHERE conrelid = '"{table}"'::regclass AND conname = '{foreignKey}') THEN
                    ALTER TABLE "{table}" ADD CONSTRAINT "{foreignKey}"
                        FOREIGN KEY ("EquityListingId") REFERENCES "EquityListing"("Id") ON DELETE RESTRICT NOT VALID;
                END IF;
                INSERT INTO "NativeListingObservationMigrationProgress" ("TableName") VALUES ('{table}')
                    ON CONFLICT ("TableName") DO NOTHING;
            END $setup$;
            """,
            suppressTransaction: true
        );
        migration.Sql(Backfill(table), suppressTransaction: true);
        migration.Sql(
            $"""
            DO $index$ BEGIN
                SET LOCAL lock_timeout = '5s';
                IF EXISTS (SELECT 1 FROM pg_index WHERE indexrelid = to_regclass('"{index}"')
                    AND indrelid = '"{table}"'::regclass AND NOT indisvalid) THEN
                    DROP INDEX "{index}";
                END IF;
            END $index$;
            """
        );
        migration.Sql(
            $"""
            CREATE UNIQUE INDEX CONCURRENTLY IF NOT EXISTS "{index}" ON "{table}" ("EquityListingId", "{dateColumn}");
            """,
            suppressTransaction: true
        );
        migration.Sql(
            $"""
            DO $required$ BEGIN
                SET LOCAL lock_timeout = '5s';
                IF NOT EXISTS (SELECT 1 FROM pg_constraint
                    WHERE conrelid = '"{table}"'::regclass AND conname = '{required}') THEN
                    ALTER TABLE "{table}" ADD CONSTRAINT "{required}" CHECK ("EquityListingId" IS NOT NULL) NOT VALID;
                END IF;
            END $required$;
            """
        );
        migration.Sql(
            $"""ALTER TABLE "{table}" VALIDATE CONSTRAINT "{required}";""",
            suppressTransaction: true
        );
        migration.Sql(
            $"""ALTER TABLE "{table}" VALIDATE CONSTRAINT "{foreignKey}";""",
            suppressTransaction: true
        );
        migration.Sql(
            $"""
            SET LOCAL lock_timeout = '5s';
            ALTER TABLE "{table}" ALTER COLUMN "EquityListingId" SET NOT NULL;
            """
        );
    }

    private static string Backfill(string table) =>
        $"""
            DO $backfill$
            DECLARE previous_id uuid; batch_ids uuid[]; updated bigint; completed boolean;
            BEGIN
                SELECT "AfterId", "Completed" INTO previous_id, completed
                    FROM "NativeListingObservationMigrationProgress" WHERE "TableName" = '{table}';
                IF completed THEN RETURN; END IF;
                LOOP
                    IF previous_id IS NULL THEN
                        SELECT array_agg("Id" ORDER BY "Id") INTO batch_ids FROM (SELECT "Id" FROM "{table}" ORDER BY "Id" LIMIT 10000) batch;
                    ELSE
                        SELECT array_agg("Id" ORDER BY "Id") INTO batch_ids FROM (SELECT "Id" FROM "{table}" WHERE "Id" > previous_id ORDER BY "Id" LIMIT 10000) batch;
                    END IF;
                    IF batch_ids IS NULL THEN
                        UPDATE "NativeListingObservationMigrationProgress" SET "Completed" = true WHERE "TableName" = '{table}';
                        COMMIT;
                        RETURN;
                    END IF;
                    UPDATE "{table}" observation SET "EquityListingId" = mapping."EquityListingId"
                    FROM "LegacyEquityListing" mapping
                    WHERE observation."Id" = ANY(batch_ids)
                        AND observation."EquityListingId" IS NULL
                        AND mapping."CommonStockId" = observation."CommonStockId"
                        AND mapping."ListedTicker" = observation."ListedTicker";
                    GET DIAGNOSTICS updated = ROW_COUNT;
                    IF EXISTS (SELECT 1 FROM "{table}" WHERE "Id" = ANY(batch_ids) AND "EquityListingId" IS NULL) THEN
                        RAISE EXCEPTION '{table} history has unresolved listing identities; the incomplete batch was not changed.';
                    END IF;
                    UPDATE "NativeListingObservationMigrationProgress"
                        SET "AfterId" = batch_ids[array_length(batch_ids, 1)], "UpdatedRows" = "UpdatedRows" + updated WHERE "TableName" = '{table}';
                    COMMIT;
                    previous_id := batch_ids[array_length(batch_ids, 1)];
                END LOOP;
            END $backfill$;
            """;
}

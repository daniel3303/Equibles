using Microsoft.EntityFrameworkCore.Migrations;

namespace Equibles.Migrations.Infrastructure;

// Frozen with the 2026-09-12 migration: later migrations must not change replay behavior.
internal static class CanonicalEquityOwnerExpansion20260912
{
    public static void Apply(
        MigrationBuilder migration,
        IReadOnlyList<EquityOwnerColumnExpansion> tables
    )
    {
        migration.Sql(
            """
            CREATE TABLE IF NOT EXISTS "EquityOwnerMigrationProgress" (
                "TableName" text PRIMARY KEY,
                "AfterKey" jsonb NULL,
                "Completed" boolean NOT NULL DEFAULT false,
                "UpdatedRows" bigint NOT NULL DEFAULT 0
            );
            """
        );
        foreach (var previousColumn in tables.Select(table => table.PreviousColumn).Distinct())
            migration.Sql(MirrorFunction(previousColumn));
        if (tables.Any(table => table.Table == "StockSplit"))
            migration.Sql(
                """
                CREATE OR REPLACE FUNCTION public.eq_sync_legacy_series()
                RETURNS trigger LANGUAGE plpgsql AS $series$
                BEGIN
                    IF TG_OP = 'UPDATE' THEN
                        IF to_jsonb(OLD)->>'CommonStockId' IS NOT DISTINCT FROM to_jsonb(NEW)->>'CommonStockId'
                            AND to_jsonb(OLD)->>TG_ARGV[0] IS NOT DISTINCT FROM to_jsonb(NEW)->>TG_ARGV[0] THEN
                            RETURN NEW;
                        END IF;
                    END IF;
                    PERFORM public.eq_ensure_legacy_listing((to_jsonb(NEW)->>'CommonStockId')::uuid,
                        to_jsonb(NEW)->>TG_ARGV[0]);
                    RETURN NEW;
                END $series$;
                """
            );
        foreach (var table in tables)
            migration.Sql(ExpandTable(table), suppressTransaction: true);
        foreach (var table in tables)
        {
            migration.Sql(Backfill(table), suppressTransaction: true);
            foreach (var index in table.Indexes)
            {
                // A failed concurrent build leaves an invalid index; valid completed builds survive a retry.
                migration.Sql(
                    $"""
                    DO $index$ BEGIN
                        IF EXISTS (SELECT 1 FROM pg_index
                            WHERE indexrelid = to_regclass({Literal(Quote(index.Name))})
                                AND indrelid = {Literal(
                        Quote(table.Table)
                    )}::regclass AND NOT indisvalid) THEN
                            SET LOCAL lock_timeout = '5s';
                            DROP INDEX {Quote(index.Name)};
                        END IF;
                    END $index$;
                    """
                );
                migration.Sql(
                    index.CreateSql.Replace(
                        "INDEX CONCURRENTLY ",
                        "INDEX CONCURRENTLY IF NOT EXISTS ",
                        StringComparison.Ordinal
                    ),
                    suppressTransaction: true
                );
            }
            foreach (var constraint in table.Constraints)
            {
                migration.Sql(
                    $"""
                    DO $constraint$ BEGIN
                        SET LOCAL lock_timeout = '5s';
                        IF NOT EXISTS (SELECT 1 FROM pg_constraint
                            WHERE conrelid = {Literal(
                        Quote(table.Table)
                    )}::regclass AND conname = {Literal(constraint.Name)}) THEN
                            ALTER TABLE {Quote(table.Table)} ADD CONSTRAINT {Quote(
                        constraint.Name
                    )} {constraint.Definition} NOT VALID;
                        END IF;
                    END $constraint$;
                    """
                );
                // Flush the ADD lock before the full validation scan; validation admits normal writes.
                migration.Sql(
                    $"ALTER TABLE {Quote(table.Table)} VALIDATE CONSTRAINT {Quote(constraint.Name)};",
                    suppressTransaction: true
                );
            }
            if (table.Required)
                migration.Sql(
                    $"SET LOCAL lock_timeout = '5s'; ALTER TABLE {Quote(table.Table)} ALTER COLUMN \"EquityIssuerId\" SET NOT NULL;"
                );
        }
    }

    private static string ExpandTable(EquityOwnerColumnExpansion table) =>
        $"""
            DO $expand$
            DECLARE existing_trigger record;
            BEGIN
                SET LOCAL lock_timeout = '5s';
                ALTER TABLE {Quote(table.Table)} ADD COLUMN IF NOT EXISTS "EquityIssuerId" uuid;
                CREATE OR REPLACE TRIGGER "00_equity_owner_columns"
                BEFORE INSERT OR UPDATE ON {Quote(table.Table)} FOR EACH ROW
                EXECUTE FUNCTION {Quote(MirrorName(table.PreviousColumn))}();
                -- UPDATE OF uses the original statement's targets, not changes made by a BEFORE trigger.
                FOR existing_trigger IN
                    SELECT trigger.oid, trigger.tgname, trigger.tgenabled
                    FROM pg_trigger trigger
                    JOIN pg_attribute previous ON previous.attrelid = trigger.tgrelid
                        AND previous.attname = {Literal(table.PreviousColumn)}
                    JOIN pg_attribute canonical ON canonical.attrelid = trigger.tgrelid
                        AND canonical.attname = 'EquityIssuerId'
                    WHERE trigger.tgrelid = {Literal(Quote(table.Table))}::regclass
                        AND NOT trigger.tgisinternal
                        AND previous.attnum = ANY(trigger.tgattr)
                        AND NOT canonical.attnum = ANY(trigger.tgattr)
                LOOP
                    EXECUTE replace(replace(pg_get_triggerdef(existing_trigger.oid),
                        'CREATE TRIGGER ', 'CREATE OR REPLACE TRIGGER '),
                        'UPDATE OF ', 'UPDATE OF "EquityIssuerId", ');
                    EXECUTE format('ALTER TABLE %I %s TRIGGER %I', {Literal(table.Table)},
                        CASE existing_trigger.tgenabled
                            WHEN 'D' THEN 'DISABLE'
                            WHEN 'R' THEN 'ENABLE REPLICA'
                            WHEN 'A' THEN 'ENABLE ALWAYS'
                            ELSE 'ENABLE' END, existing_trigger.tgname);
                END LOOP;
            END $expand$;
            """;

    private static string MirrorFunction(string previousColumn) =>
        $"""
            CREATE OR REPLACE FUNCTION {Quote(
                MirrorName(previousColumn)
            )}() RETURNS trigger LANGUAGE plpgsql AS $mirror$
            BEGIN
                IF TG_OP = 'INSERT' THEN
                    IF NEW."EquityIssuerId" IS NULL THEN
                        NEW."EquityIssuerId" := NEW.{Quote(previousColumn)};
                    ELSIF NEW.{Quote(previousColumn)} IS NULL THEN
                        NEW.{Quote(previousColumn)} := NEW."EquityIssuerId";
                    ELSIF NEW."EquityIssuerId" IS DISTINCT FROM NEW.{Quote(previousColumn)} THEN
                        RAISE EXCEPTION 'Canonical and previous issuer owners disagree on %', TG_TABLE_NAME;
                    END IF;
                ELSIF NEW."EquityIssuerId" IS DISTINCT FROM OLD."EquityIssuerId" THEN
                    IF NEW.{Quote(previousColumn)} IS DISTINCT FROM OLD.{Quote(previousColumn)}
                        AND NEW.{Quote(previousColumn)} IS DISTINCT FROM NEW."EquityIssuerId" THEN
                        RAISE EXCEPTION 'Concurrent issuer owner changes disagree on %', TG_TABLE_NAME;
                    END IF;
                    NEW.{Quote(previousColumn)} := NEW."EquityIssuerId";
                ELSIF NEW.{Quote(previousColumn)} IS DISTINCT FROM OLD.{Quote(previousColumn)} THEN
                    NEW."EquityIssuerId" := NEW.{Quote(previousColumn)};
                ELSIF NEW."EquityIssuerId" IS NULL THEN
                    NEW."EquityIssuerId" := NEW.{Quote(previousColumn)};
                ELSIF NEW."EquityIssuerId" IS DISTINCT FROM NEW.{Quote(previousColumn)} THEN
                    RAISE EXCEPTION 'Stored issuer owners disagree on %', TG_TABLE_NAME;
                END IF;
                RETURN NEW;
            END $mirror$;
            """;

    private static string Backfill(EquityOwnerColumnExpansion table)
    {
        var keys = string.Join(", ", table.PrimaryKey.Select(Quote));
        var descending = string.Join(", ", table.PrimaryKey.Select(key => Quote(key) + " DESC"));
        var previousKeys = string.Join(
            ", ",
            table.PrimaryKey.Select(key => "previous_row." + Quote(key))
        );
        var endKeys = string.Join(", ", table.PrimaryKey.Select(key => "batch_end." + Quote(key)));
        var keyJson = string.Join(
            ", ",
            table.PrimaryKey.Select(key => Literal(key) + ", batch_end." + Quote(key))
        );
        return $"""
            DO $backfill$
            DECLARE
                previous_row {Quote(table.Table)}%ROWTYPE;
                batch_end {Quote(table.Table)}%ROWTYPE;
                previous_key jsonb;
                updated bigint;
                completed boolean;
            BEGIN
                SELECT "AfterKey", "Completed" INTO previous_key, completed
                FROM "EquityOwnerMigrationProgress" WHERE "TableName" = {Literal(table.Table)};
                IF completed THEN RETURN; END IF;
                previous_row := jsonb_populate_record(NULL::{Quote(table.Table)}, previous_key);
                LOOP
                    IF previous_key IS NULL THEN
                        SELECT * INTO batch_end FROM (
                            SELECT * FROM {Quote(table.Table)} ORDER BY {keys} LIMIT 10000
                        ) batch ORDER BY {descending} LIMIT 1;
                    ELSE
                        SELECT * INTO batch_end FROM (
                            SELECT * FROM {Quote(table.Table)}
                            WHERE ROW({keys}) > ROW({previousKeys}) ORDER BY {keys} LIMIT 10000
                        ) batch ORDER BY {descending} LIMIT 1;
                    END IF;
                    IF NOT FOUND THEN
                        INSERT INTO "EquityOwnerMigrationProgress" ("TableName", "AfterKey", "Completed")
                        VALUES ({Literal(table.Table)}, previous_key, true)
                        ON CONFLICT ("TableName") DO UPDATE SET "Completed" = true;
                        COMMIT;
                        EXIT;
                    END IF;
                    IF previous_key IS NULL THEN
                        UPDATE {Quote(table.Table)} SET "EquityIssuerId" = {Quote(
                table.PreviousColumn
            )}
                        WHERE ROW({keys}) <= ROW({endKeys})
                            AND "EquityIssuerId" IS DISTINCT FROM {Quote(table.PreviousColumn)};
                    ELSE
                        UPDATE {Quote(table.Table)} SET "EquityIssuerId" = {Quote(
                table.PreviousColumn
            )}
                        WHERE ROW({keys}) > ROW({previousKeys}) AND ROW({keys}) <= ROW({endKeys})
                            AND "EquityIssuerId" IS DISTINCT FROM {Quote(table.PreviousColumn)};
                    END IF;
                    GET DIAGNOSTICS updated = ROW_COUNT;
                    previous_key := jsonb_build_object({keyJson});
                    INSERT INTO "EquityOwnerMigrationProgress" ("TableName", "AfterKey", "UpdatedRows")
                    VALUES ({Literal(table.Table)}, previous_key, updated)
                    ON CONFLICT ("TableName") DO UPDATE SET
                        "AfterKey" = EXCLUDED."AfterKey",
                        "UpdatedRows" = "EquityOwnerMigrationProgress"."UpdatedRows" + EXCLUDED."UpdatedRows";
                    previous_row := batch_end;
                    COMMIT;
                END LOOP;
            END $backfill$;
            """;
    }

    private static string MirrorName(string column) =>
        "eq_mirror_canonical_owner_" + column.ToLowerInvariant();

    private static string Quote(string identifier) => '"' + identifier.Replace("\"", "\"\"") + '"';

    private static string Literal(string value) => '\'' + value.Replace("'", "''") + '\'';
}

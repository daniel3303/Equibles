using Microsoft.EntityFrameworkCore.Migrations;

namespace Equibles.Migrations.Infrastructure;

// Capture original versions and protect unretargeted children before any long backfill starts.
internal static class NativeDirectoryEvidenceExpansion20260912
{
    public static void Apply(MigrationBuilder migration) =>
        migration.Sql(ExpansionSql, suppressTransaction: true);

    private const string ExpansionSql = """
        DO $setup$
        BEGIN
        SET LOCAL lock_timeout = '5s';
        CREATE TABLE IF NOT EXISTS "EquityDirectorySourceRecord" (
            "Id" uuid NOT NULL,
            "Source" character varying(64) NOT NULL,
            "SourceRecordKey" character varying(256) NOT NULL,
            "PayloadHash" character varying(64) NOT NULL,
            "PayloadJson" jsonb NOT NULL,
            "CapturedAt" timestamp with time zone NOT NULL,
            CONSTRAINT "PK_EquityDirectorySourceRecord" PRIMARY KEY ("Id")
        );

        CREATE UNIQUE INDEX IF NOT EXISTS "IX_EquityDirectorySourceRecord_Source_SourceRecordKey_PayloadH~" ON "EquityDirectorySourceRecord" ("Source", "SourceRecordKey", "PayloadHash");
        LOCK TABLE "CommonStock" IN SHARE ROW EXCLUSIVE MODE;
        SET LOCAL timezone = 'UTC';
        SET LOCAL extra_float_digits = 3;

        CREATE OR REPLACE FUNCTION eq_capture_original_directory_record(source_row jsonb) RETURNS void
        LANGUAGE plpgsql AS $capture$
        DECLARE
            source_key text := source_row->>'Id';
            payload_hash text := encode(sha256(convert_to(source_row::text, 'UTF8')), 'hex');
        BEGIN
            IF jsonb_typeof(source_row) <> 'object' OR source_key IS NULL THEN
                RAISE EXCEPTION 'A complete directory source row is required';
            END IF;
            INSERT INTO "EquityDirectorySourceRecord"
                ("Id", "Source", "SourceRecordKey", "PayloadHash", "PayloadJson", "CapturedAt")
            VALUES (gen_random_uuid(), 'common-stock-v1', source_key, payload_hash, source_row, CURRENT_TIMESTAMP)
            ON CONFLICT ("Source", "SourceRecordKey", "PayloadHash") DO NOTHING;
            IF NOT EXISTS (
                SELECT 1 FROM "EquityDirectorySourceRecord"
                WHERE "Source" = 'common-stock-v1' AND "SourceRecordKey" = source_key
                  AND "PayloadHash" = payload_hash AND "PayloadJson" = source_row
            ) THEN
                RAISE EXCEPTION 'Directory source hash conflict; original data was not preserved';
            END IF;
        END;
        $capture$;

        PERFORM eq_capture_original_directory_record(to_jsonb(source)) FROM "CommonStock" source;

        CREATE OR REPLACE FUNCTION eq_record_original_directory_change() RETURNS trigger
        LANGUAGE plpgsql SET timezone = 'UTC' SET extra_float_digits = 3 AS $capture$
        BEGIN
            IF TG_OP <> 'INSERT' THEN
                PERFORM eq_capture_original_directory_record(to_jsonb(OLD));
            END IF;
            IF TG_OP <> 'DELETE' THEN
                PERFORM eq_capture_original_directory_record(to_jsonb(NEW));
                RETURN NEW;
            END IF;
            RETURN OLD;
        END;
        $capture$;
        CREATE OR REPLACE TRIGGER equity_original_directory_evidence AFTER INSERT OR UPDATE OR DELETE ON "CommonStock"
            FOR EACH ROW EXECUTE FUNCTION eq_record_original_directory_change();

        CREATE OR REPLACE FUNCTION eq_protect_directory_source_evidence() RETURNS trigger
        LANGUAGE plpgsql AS $protect$
        BEGIN
            RAISE EXCEPTION 'Directory source evidence is immutable';
        END;
        $protect$;
        CREATE OR REPLACE TRIGGER equity_directory_source_evidence_immutable BEFORE UPDATE OR DELETE ON "EquityDirectorySourceRecord"
            FOR EACH ROW EXECUTE FUNCTION eq_protect_directory_source_evidence();
        CREATE OR REPLACE TRIGGER equity_directory_source_evidence_no_truncate BEFORE TRUNCATE ON "EquityDirectorySourceRecord"
            FOR EACH STATEMENT EXECUTE FUNCTION eq_protect_directory_source_evidence();
        CREATE OR REPLACE FUNCTION eq_guard_unmigrated_directory_history() RETURNS trigger
        LANGUAGE plpgsql AS $guard$
        DECLARE reference record; has_history boolean;
        BEGIN
            FOR reference IN
                SELECT foreign_key.conrelid AS table_id, foreign_key.conname AS constraint_name,
                    column_row.attname AS column_name, array_length(foreign_key.conkey, 1) AS key_count
                FROM pg_constraint foreign_key
                JOIN pg_attribute column_row ON column_row.attrelid = foreign_key.conrelid
                    AND column_row.attnum = foreign_key.conkey[1]
                WHERE foreign_key.contype = 'f' AND foreign_key.confrelid = TG_RELID
                    AND foreign_key.confdeltype IN ('c', 'n', 'd')
                    AND NOT (foreign_key.conrelid = COALESCE(to_regclass('"EquityIssuer"'), 0::oid)
                        AND foreign_key.conname = 'FK_EquityIssuer_CommonStock_CommonStockId'
                        AND column_row.attname = 'CommonStockId' AND foreign_key.confdeltype = 'n')
                ORDER BY foreign_key.conrelid, foreign_key.conname
            LOOP
                IF reference.key_count <> 1 THEN
                    RAISE EXCEPTION 'Unmigrated composite directory ownership must be preserved before retirement'
                        USING ERRCODE = '23503';
                END IF;
                EXECUTE format('SELECT EXISTS (SELECT 1 FROM %s WHERE %I = $1)',
                    reference.table_id::regclass, reference.column_name)
                    INTO has_history USING OLD."Id";
                IF has_history THEN
                    RAISE EXCEPTION 'Directory retirement would change unmigrated history protected by %', reference.constraint_name
                        USING ERRCODE = '23503';
                END IF;
            END LOOP;
            RETURN OLD;
        END $guard$;
        CREATE OR REPLACE TRIGGER equity_unmigrated_directory_history BEFORE DELETE ON "CommonStock"
            FOR EACH ROW EXECUTE FUNCTION eq_guard_unmigrated_directory_history();

        END $setup$;
        """;
}

using Microsoft.EntityFrameworkCore.Migrations;

namespace Equibles.Migrations.Migrations;

public partial class PreserveRetiringEquityIdentitySources : Migration {
    protected override void Up(MigrationBuilder migrationBuilder) {
        // Detach old cursor fields from the model, but retain physical columns until old workers retire.
        // Capture complete original rows and subsequent versions before removing compatibility storage.
        migrationBuilder.Sql("""
            DO $setup$
            BEGIN
                SET LOCAL lock_timeout = '5s';
                SET LOCAL timezone = 'UTC';
                SET LOCAL extra_float_digits = 3;
                LOCK TABLE "LegacyEquityListing", "CorporateActionPriceReconciliationCursor" IN SHARE ROW EXCLUSIVE MODE;

                CREATE OR REPLACE FUNCTION eq_capture_retiring_identity_source(source_kind text, source_key text, source_row jsonb)
                RETURNS void LANGUAGE plpgsql AS $capture$
                DECLARE payload_hash text := encode(sha256(convert_to(source_row::text, 'UTF8')), 'hex');
                BEGIN
                    IF source_kind IS NULL OR source_key IS NULL OR jsonb_typeof(source_row) IS DISTINCT FROM 'object' THEN
                        RAISE EXCEPTION 'A complete identity source row and key are required';
                    END IF;
                    INSERT INTO "EquityDirectorySourceRecord"
                        ("Id", "Source", "SourceRecordKey", "PayloadHash", "PayloadJson", "CapturedAt")
                    VALUES (gen_random_uuid(), source_kind, source_key, payload_hash, source_row, CURRENT_TIMESTAMP)
                    ON CONFLICT ("Source", "SourceRecordKey", "PayloadHash") DO NOTHING;
                    IF NOT EXISTS (
                        SELECT 1 FROM "EquityDirectorySourceRecord"
                        WHERE "Source" = source_kind AND "SourceRecordKey" = source_key
                          AND "PayloadHash" = payload_hash AND "PayloadJson" = source_row
                    ) THEN
                        RAISE EXCEPTION 'Identity source hash conflict; original data was not preserved';
                    END IF;
                END;
                $capture$;

                CREATE OR REPLACE FUNCTION eq_record_retiring_listing_identity() RETURNS trigger
                LANGUAGE plpgsql SET timezone = 'UTC' SET extra_float_digits = 3 AS $capture$
                BEGIN
                    IF TG_OP <> 'INSERT' THEN
                        PERFORM eq_capture_retiring_identity_source('legacy-equity-listing-v1',
                            jsonb_build_array(OLD."CommonStockId", OLD."ListedTicker")::text, to_jsonb(OLD));
                    END IF;
                    IF TG_OP <> 'DELETE' THEN
                        PERFORM eq_capture_retiring_identity_source('legacy-equity-listing-v1',
                            jsonb_build_array(NEW."CommonStockId", NEW."ListedTicker")::text, to_jsonb(NEW));
                        RETURN NEW;
                    END IF;
                    RETURN OLD;
                END;
                $capture$;
                CREATE OR REPLACE TRIGGER equity_retiring_listing_source AFTER INSERT OR UPDATE OR DELETE ON "LegacyEquityListing"
                    FOR EACH ROW EXECUTE FUNCTION eq_record_retiring_listing_identity();

                CREATE OR REPLACE FUNCTION eq_record_retiring_cursor_identity() RETURNS trigger
                LANGUAGE plpgsql SET timezone = 'UTC' SET extra_float_digits = 3 AS $capture$
                BEGIN
                    IF TG_OP <> 'INSERT' THEN
                        PERFORM eq_capture_retiring_identity_source('corporate-action-cursor-v1', OLD."Name", to_jsonb(OLD));
                    END IF;
                    IF TG_OP <> 'DELETE' THEN
                        PERFORM eq_capture_retiring_identity_source('corporate-action-cursor-v1', NEW."Name", to_jsonb(NEW));
                        RETURN NEW;
                    END IF;
                    RETURN OLD;
                END;
                $capture$;
                CREATE OR REPLACE TRIGGER equity_retiring_cursor_source AFTER INSERT OR UPDATE OR DELETE ON "CorporateActionPriceReconciliationCursor"
                    FOR EACH ROW EXECUTE FUNCTION eq_record_retiring_cursor_identity();

                PERFORM eq_capture_retiring_identity_source('legacy-equity-listing-v1',
                    jsonb_build_array(r."CommonStockId", r."ListedTicker")::text, to_jsonb(r)) FROM "LegacyEquityListing" r;
                PERFORM eq_capture_retiring_identity_source('corporate-action-cursor-v1', r."Name", to_jsonb(r))
                    FROM "CorporateActionPriceReconciliationCursor" r;
            END;
            $setup$;
            """, suppressTransaction: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Retained identity source evidence must not be discarded; roll forward instead.");
}

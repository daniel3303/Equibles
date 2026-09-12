using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class PreserveOriginalDirectoryEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EquityDirectorySourceRecord",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Source = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SourceRecordKey = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    PayloadHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PayloadJson = table.Column<string>(type: "jsonb", nullable: false),
                    CapturedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EquityDirectorySourceRecord", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EquityDirectorySourceRecord_Source_SourceRecordKey_PayloadH~",
                table: "EquityDirectorySourceRecord",
                columns: new[] { "Source", "SourceRecordKey", "PayloadHash" },
                unique: true);
            migrationBuilder.Sql("""
                LOCK TABLE "CommonStock" IN SHARE ROW EXCLUSIVE MODE;
                SET LOCAL timezone = 'UTC';
                SET LOCAL extra_float_digits = 3;

                CREATE FUNCTION eq_capture_original_directory_record(source_row jsonb) RETURNS void
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

                SELECT eq_capture_original_directory_record(to_jsonb(source)) FROM "CommonStock" source;

                CREATE FUNCTION eq_record_original_directory_change() RETURNS trigger
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
                CREATE TRIGGER equity_original_directory_evidence AFTER INSERT OR UPDATE OR DELETE ON "CommonStock"
                    FOR EACH ROW EXECUTE FUNCTION eq_record_original_directory_change();

                CREATE FUNCTION eq_protect_directory_source_evidence() RETURNS trigger
                LANGUAGE plpgsql AS $protect$
                BEGIN
                    RAISE EXCEPTION 'Directory source evidence is immutable';
                END;
                $protect$;
                CREATE TRIGGER equity_directory_source_evidence_immutable BEFORE UPDATE OR DELETE ON "EquityDirectorySourceRecord"
                    FOR EACH ROW EXECUTE FUNCTION eq_protect_directory_source_evidence();
                CREATE TRIGGER equity_directory_source_evidence_no_truncate BEFORE TRUNCATE ON "EquityDirectorySourceRecord"
                    FOR EACH STATEMENT EXECUTE FUNCTION eq_protect_directory_source_evidence();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new InvalidOperationException("Rollback would discard original directory evidence; roll forward instead.");
        }
    }
}

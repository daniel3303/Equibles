using Microsoft.EntityFrameworkCore.Migrations;

namespace Equibles.Migrations.Infrastructure;

// Versioned SQL is migration history: later index changes need a new migration.
public static class QwenHalfvecIndex20260930
{
    public const string CreateSql = """
        CREATE INDEX CONCURRENTLY IF NOT EXISTS "IX_Embedding_Qwen3_Halfvec1024_Hnsw"
        ON public."Embedding" USING hnsw (("Vector"::public.halfvec(1024)) public.halfvec_cosine_ops)
        WITH (m = 16, ef_construction = 64)
        WHERE "Model" = 'qwen3-embedding:0.6b' AND "VectorDimension" = 1024;
        """;

    public static void Up(MigrationBuilder migration)
    {
        migration.Sql(Validate(required: false), suppressTransaction: true);
        migration.Sql(CreateSql, suppressTransaction: true);
        migration.Sql(Validate(required: true), suppressTransaction: true);
    }

    public static void Down(MigrationBuilder migration)
    {
        migration.Sql(Validate(required: false), suppressTransaction: true);
        migration.Sql(
            """
            DROP INDEX CONCURRENTLY IF EXISTS public."IX_Embedding_Qwen3_Halfvec1024_Hnsw";
            """,
            suppressTransaction: true
        );
    }

    private static string Validate(bool required) =>
        $$"""
            DO $halfvec_migration$
            DECLARE
                target oid := to_regclass('public."IX_Embedding_Qwen3_Halfvec1024_Hnsw"');
            BEGIN
                -- The manual builder holds this session lock until its final verification.
                -- A transaction lock here cannot leak into a pooled connection after failure.
                IF NOT pg_try_advisory_xact_lock(837421, 1024) THEN
                    RAISE EXCEPTION 'Halfvec index build is running; retry the migration after it completes';
                END IF;
                IF EXISTS (SELECT FROM pg_stat_progress_create_index
                           WHERE relid = 'public."Embedding"'::regclass) THEN
                    RAISE EXCEPTION 'Embedding has an active index build; retry the migration after it completes';
                END IF;
                IF target IS NULL THEN
                    IF {{(required ? "true" : "false")}} THEN
                        RAISE EXCEPTION 'Required halfvec index is missing';
                    END IF;
                    RETURN;
                END IF;
                -- Deparse in a fixed path so ambient connection settings cannot change the proof.
                PERFORM set_config('search_path', 'public, pg_catalog', true);
                IF NOT EXISTS (
                    SELECT FROM pg_index i
                    JOIN pg_class c ON c.oid = i.indexrelid
                    JOIN pg_am am ON am.oid = c.relam
                    JOIN pg_opclass op ON op.oid = i.indclass[0]
                    WHERE i.indexrelid = target
                      AND i.indrelid = 'public."Embedding"'::regclass
                      AND c.relkind = 'i' AND am.amname = 'hnsw'
                      AND i.indisvalid AND i.indisready AND i.indislive
                      AND NOT i.indisunique AND NOT i.indisexclusion
                      AND i.indnkeyatts = 1 AND i.indnatts = 1
                      AND i.indkey[0] = 0 AND i.indoption[0] = 0
                      AND op.opcname = 'halfvec_cosine_ops'
                      AND op.opcnamespace = 'public'::regnamespace
                      AND pg_get_expr(i.indexprs, i.indrelid, false) = '("Vector")::halfvec(1024)'
                      AND pg_get_expr(i.indpred, i.indrelid, false) =
                          '((("Model")::text = ''qwen3-embedding:0.6b''::text) AND ("VectorDimension" = 1024))'
                      AND c.reloptions @> ARRAY['m=16', 'ef_construction=64']::text[]
                      AND cardinality(c.reloptions) = 2
                ) THEN
                    RAISE EXCEPTION 'Halfvec index is invalid or its definition differs; inspect it without dropping or rebuilding it automatically';
                END IF;
            END
            $halfvec_migration$;
            """;
}

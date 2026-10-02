using Microsoft.EntityFrameworkCore.Migrations;

namespace Equibles.Migrations.Infrastructure;

// Versioned SQL is migration history: a different index definition needs a new migration.
public static class QwenHalfvecIndex20260930
{
    // A larger table outlives the migration command timeout, and a cancelled build leaves an invalid index.
    public const int MaxInlineBuildRows = 100_000;

    private const string Predicate = """
        "Model" = 'qwen3-embedding:0.6b' AND "VectorDimension" = 1024
        """;

    private const string Definition = $"""
        USING hnsw (("Vector"::public.halfvec(1024)) public.halfvec_cosine_ops)
        WITH (m = 16, ef_construction = 64)
        WHERE {Predicate}
        """;

    public const string CreateSql = $"""
        CREATE INDEX CONCURRENTLY IF NOT EXISTS "IX_Embedding_Qwen3_Halfvec1024_Hnsw"
        ON public."Embedding" {Definition};
        """;

    private const string RefuseActiveBuild = """
        -- A manual builder holds this session lock until its final verification.
        -- A transaction lock here cannot leak into a pooled connection after failure.
        IF NOT pg_try_advisory_xact_lock(837421, 1024) THEN
            RAISE EXCEPTION 'Halfvec index build is running; retry the migration after it completes';
        END IF;
        IF EXISTS (SELECT FROM pg_stat_progress_create_index
                   WHERE relid = 'public."Embedding"'::regclass) THEN
            RAISE EXCEPTION 'Embedding has an active index build; retry the migration after it completes';
        END IF;
        """;

    private static readonly string RequirePrebuildOnLargeTable = $"""
        IF (SELECT count(*) FROM (SELECT FROM public."Embedding" WHERE {Predicate}
                                  LIMIT {MaxInlineBuildRows} + 1) bounded) > {MaxInlineBuildRows} THEN
            RAISE EXCEPTION 'Embedding is too large to index inside a migration; build the halfvec index manually, then retry';
        END IF;
        RETURN;
        """;

    private const string RaiseMissing = """
        RAISE EXCEPTION 'Required halfvec index is missing';
        """;

    // The server deparses the versioned definition itself, so the proof holds on any PostgreSQL version and column type.
    private const string ProveDefinition = $"""
        CREATE TEMP TABLE halfvec_index_reference (LIKE public."Embedding");
        CREATE INDEX halfvec_index_reference_idx ON pg_temp.halfvec_index_reference {Definition};
        IF NOT EXISTS (
            SELECT FROM pg_index i
            JOIN pg_class c ON c.oid = i.indexrelid
            JOIN pg_index r ON r.indexrelid = 'pg_temp.halfvec_index_reference_idx'::regclass
            JOIN pg_class rc ON rc.oid = r.indexrelid
            WHERE i.indexrelid = target
              AND i.indrelid = 'public."Embedding"'::regclass
              AND c.relkind = 'i' AND c.relam = rc.relam
              AND i.indisvalid AND i.indisready AND i.indislive
              AND i.indisunique = r.indisunique AND i.indisexclusion = r.indisexclusion
              AND i.indnkeyatts = r.indnkeyatts AND i.indnatts = r.indnatts
              AND i.indkey = r.indkey AND i.indoption = r.indoption
              AND i.indclass = r.indclass AND i.indcollation = r.indcollation
              AND pg_get_expr(i.indexprs, i.indrelid) = pg_get_expr(r.indexprs, r.indrelid)
              AND pg_get_expr(i.indpred, i.indrelid) = pg_get_expr(r.indpred, r.indrelid)
              AND c.reloptions @> rc.reloptions AND rc.reloptions @> c.reloptions
        ) THEN
            RAISE EXCEPTION 'Halfvec index is invalid or its definition differs; inspect it without dropping or rebuilding it automatically';
        END IF;
        DROP TABLE pg_temp.halfvec_index_reference;
        """;

    public static void Up(MigrationBuilder migration)
    {
        migration.Sql(Validate(RequirePrebuildOnLargeTable), suppressTransaction: true);
        migration.Sql(CreateSql, suppressTransaction: true);
        migration.Sql(Validate(RaiseMissing), suppressTransaction: true);
    }

    public static void Down(MigrationBuilder migration)
    {
        // Rollback also clears an invalid leftover, which is safe once no builder owns it.
        migration.Sql(Block(RefuseActiveBuild), suppressTransaction: true);
        migration.Sql(
            """
            DROP INDEX CONCURRENTLY IF EXISTS public."IX_Embedding_Qwen3_Halfvec1024_Hnsw";
            """,
            suppressTransaction: true
        );
    }

    private static string Validate(string whenMissing) =>
        Block(
            $"""
            {RefuseActiveBuild}
            IF target IS NULL THEN
                {whenMissing}
            END IF;
            {ProveDefinition}
            """
        );

    private static string Block(string body) =>
        $"""
            DO $halfvec_migration$
            DECLARE
                target oid := to_regclass('public."IX_Embedding_Qwen3_Halfvec1024_Hnsw"');
            BEGIN
                {body}
            END
            $halfvec_migration$;
            """;
}

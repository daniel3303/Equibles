using Microsoft.EntityFrameworkCore.Migrations;

namespace Equibles.Migrations.Infrastructure;

// Frozen migration preparation: physical locations are traversal checkpoints, never row identities.
internal static class PhysicalEquityOwnerBackfill20260913
{
    private const int BlocksPerBatch = 1024;
    private const string Checkpoint = "EquityOwnerPhysicalBackfill";
    private const string EqualityExpression =
        "(NOT (\"EquityIssuerId\" IS DISTINCT FROM \"CommonStockId\"))";

    public static void Apply(MigrationBuilder migration)
    {
        foreach (var table in new[] { "FinancialFact", "InstitutionalHolding" })
        {
            migration.Sql(Setup(table), suppressTransaction: true);
            migration.Sql(Backfill(table), suppressTransaction: true);
            migration.Sql(InstallProof(table), suppressTransaction: true);
            migration.Sql(ValidateProof(table), suppressTransaction: true);
            migration.Sql(Complete(table), suppressTransaction: true);
        }
        migration.Sql(
            $"""
            DO $cleanup$ BEGIN
                {AlreadyExpanded()}
                IF to_regclass('"{Checkpoint}"') IS NULL THEN RETURN; END IF;
                IF EXISTS (SELECT 1 FROM "{Checkpoint}") THEN
                    RAISE EXCEPTION 'Physical issuer backfill checkpoints remain incomplete';
                END IF;
                DROP TABLE "{Checkpoint}";
            END $cleanup$;
            """,
            suppressTransaction: true
        );
    }

    private static string Setup(string table)
    {
        var descriptor = new EquityOwnerColumnExpansion(
            table,
            "CommonStockId",
            ["Id"],
            true,
            [],
            []
        );
        var preparation =
            CanonicalEquityOwnerExpansion20260912.MirrorFunction("CommonStockId")
            + CanonicalEquityOwnerExpansion20260912.ExpandTable(descriptor);
        return $"""
            DO $setup$
            DECLARE completed boolean := false;
            BEGIN
                {Skip(table)}
                SET LOCAL lock_timeout = '5s';
                IF EXISTS (SELECT 1 FROM pg_class WHERE oid = '"{table}"'::regclass
                    AND (relkind <> 'r' OR relispartition))
                    OR EXISTS (SELECT 1 FROM pg_inherits WHERE inhrelid = '"{table}"'::regclass
                        OR inhparent = '"{table}"'::regclass) THEN
                    RAISE EXCEPTION 'Physical issuer backfill requires an ordinary non-inherited table: {table}';
                END IF;
                EXECUTE $preparation${preparation}$preparation$;
                IF (SELECT count(*) FROM pg_attribute WHERE attrelid = '"{table}"'::regclass
                    AND attname IN ('CommonStockId', 'EquityIssuerId') AND NOT attisdropped
                    AND atttypid = 'uuid'::regtype) <> 2 THEN
                    RAISE EXCEPTION 'Unexpected issuer column types on {table}';
                END IF;
                CREATE TABLE IF NOT EXISTS "EquityOwnerMigrationProgress" (
                    "TableName" text PRIMARY KEY, "AfterKey" jsonb NULL,
                    "Completed" boolean NOT NULL DEFAULT false, "UpdatedRows" bigint NOT NULL DEFAULT 0);
                SELECT "Completed" INTO completed FROM "EquityOwnerMigrationProgress" WHERE "TableName" = '{table}';
                CREATE TABLE IF NOT EXISTS "{Checkpoint}" (
                    "TableName" text PRIMARY KEY, "RelationOid" oid NOT NULL, "RelationFile" oid NOT NULL,
                    "BlockCount" bigint NOT NULL CHECK ("BlockCount" BETWEEN 0 AND 4294967294),
                    "NextBlock" bigint NOT NULL DEFAULT 0 CHECK ("NextBlock" BETWEEN 0 AND "BlockCount"),
                    "UpdatedRows" bigint NOT NULL DEFAULT 0 CHECK ("UpdatedRows" >= 0));
                INSERT INTO "{Checkpoint}" ("TableName", "RelationOid", "RelationFile", "BlockCount")
                SELECT '{table}', '"{table}"'::regclass, pg_relation_filenode('"{table}"'::regclass),
                    CASE WHEN completed THEN 0 ELSE
                        pg_relation_size('"{table}"'::regclass) / current_setting('block_size')::bigint END
                ON CONFLICT ("TableName") DO NOTHING;
            END $setup$;
            """;
    }

    private static string Backfill(string table) =>
        $"""
            DO $backfill$
            DECLARE state record; ending bigint; changed bigint;
            BEGIN
                {Skip(table)}
                LOOP
                    SET LOCAL lock_timeout = '5s';
                    -- Admit application writes, but exclude rewrites while a physical range is used.
                    LOCK TABLE "{table}" IN SHARE UPDATE EXCLUSIVE MODE;
                    SELECT * INTO STRICT state FROM "{Checkpoint}" WHERE "TableName" = '{table}' FOR UPDATE;
                    {ValidateRelation(table)}
                    IF state."NextBlock" = state."BlockCount" THEN EXIT; END IF;
                    ending := LEAST(state."NextBlock" + {BlocksPerBatch}, state."BlockCount");
                    -- Direct tid predicates allow Tid Range Scan; never persist individual tuple addresses.
                    UPDATE "{table}" SET "EquityIssuerId" = "CommonStockId"
                    WHERE ctid >= format('(%s,0)', state."NextBlock")::tid
                        AND ctid < format('(%s,0)', ending)::tid
                        AND "EquityIssuerId" IS NULL AND "CommonStockId" IS NOT NULL;
                    GET DIAGNOSTICS changed = ROW_COUNT;
                    UPDATE "{Checkpoint}" SET "NextBlock" = ending,
                        "UpdatedRows" = "UpdatedRows" + changed WHERE "TableName" = '{table}';
                    COMMIT;
                END LOOP;
            END $backfill$;
            """;

    private static string InstallProof(string table) =>
        $"""
            DO $proof$ BEGIN
                {Skip(table)}
                SET LOCAL lock_timeout = '5s';
                IF EXISTS (SELECT 1 FROM pg_constraint WHERE conrelid = '"{table}"'::regclass
                    AND conname = 'CK_{table}_CanonicalOwnerMirror') THEN
                    {ValidateExpression(table)}
                ELSE
                    ALTER TABLE "{table}" ADD CONSTRAINT "CK_{table}_CanonicalOwnerMirror"
                        CHECK ("EquityIssuerId" IS NOT DISTINCT FROM "CommonStockId") NOT VALID;
                END IF;
            END $proof$;
            """;

    private static string ValidateProof(string table) =>
        $"""
            DO $validate$ BEGIN
                {Skip(table)}
                SET LOCAL lock_timeout = '5s';
                {ValidateExpression(table)}
                ALTER TABLE "{table}" VALIDATE CONSTRAINT "CK_{table}_CanonicalOwnerMirror";
            END $validate$;
            """;

    private static string Complete(string table) =>
        $"""
            DO $complete$
            DECLARE state record;
            BEGIN
                {Skip(table)}
                SET LOCAL lock_timeout = '5s';
                LOCK TABLE "{table}" IN SHARE UPDATE EXCLUSIVE MODE;
                SELECT * INTO STRICT state FROM "{Checkpoint}" WHERE "TableName" = '{table}' FOR UPDATE;
                {ValidateRelation(table)}
                {ValidateExpression(table)}
                IF state."NextBlock" <> state."BlockCount" OR NOT EXISTS (
                    SELECT 1 FROM pg_constraint WHERE conrelid = '"{table}"'::regclass
                        AND conname = 'CK_{table}_CanonicalOwnerMirror' AND convalidated) THEN
                    RAISE EXCEPTION 'Physical issuer backfill proof is incomplete for {table}';
                END IF;
                INSERT INTO "EquityOwnerMigrationProgress" ("TableName", "Completed", "UpdatedRows")
                VALUES ('{table}', true, state."UpdatedRows")
                ON CONFLICT ("TableName") DO UPDATE SET "Completed" = true,
                    "UpdatedRows" = "EquityOwnerMigrationProgress"."UpdatedRows" + EXCLUDED."UpdatedRows";
                DELETE FROM "{Checkpoint}" WHERE "TableName" = '{table}';
            END $complete$;
            """;

    private static string ValidateRelation(string table) =>
        $"""
            IF state."RelationOid" <> '"{table}"'::regclass::oid
                OR state."RelationFile" <> pg_relation_filenode('"{table}"'::regclass) THEN
                RAISE EXCEPTION 'Relation changed during physical issuer backfill: {table}; reconcile before resetting its checkpoint';
            END IF;
            """;

    private static string ValidateExpression(string table) =>
        $"""
            IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conrelid = '"{table}"'::regclass
                AND conname = 'CK_{table}_CanonicalOwnerMirror' AND contype = 'c'
                AND pg_get_expr(conbin, conrelid) = '{EqualityExpression}') THEN
                RAISE EXCEPTION 'Unexpected canonical owner proof on {table}';
            END IF;
            """;

    private static string AlreadyExpanded() =>
        """
            IF to_regclass('"__EFMigrationsHistory"') IS NOT NULL THEN
                IF EXISTS (SELECT 1 FROM "__EFMigrationsHistory"
                    WHERE "MigrationId" IN ('20260912160424_ExpandCanonicalEquityOwnerColumns',
                        '20260912160519_ExpandCanonicalEquityOwnerColumns')) THEN
                    RETURN;
                END IF;
            END IF;
            """;

    private static string Skip(string table) =>
        $"""
            {AlreadyExpanded()}
            IF to_regclass('"{table}"') IS NULL THEN
                RAISE EXCEPTION 'Missing physical issuer backfill table: {table}';
            END IF;
            IF NOT EXISTS (SELECT 1 FROM pg_attribute WHERE attrelid = '"{table}"'::regclass
                AND attname = 'CommonStockId' AND NOT attisdropped) THEN RETURN; END IF;
            IF to_regclass('"EquityOwnerMigrationProgress"') IS NOT NULL THEN
                IF EXISTS (SELECT 1 FROM "EquityOwnerMigrationProgress" WHERE "TableName" = '{table}' AND "Completed")
                    AND EXISTS (SELECT 1 FROM pg_constraint WHERE conrelid = '"{table}"'::regclass
                        AND conname = 'CK_{table}_CanonicalOwnerMirror' AND contype = 'c' AND convalidated
                        AND pg_get_expr(conbin, conrelid) = '{EqualityExpression}') THEN
                    IF to_regclass('"{Checkpoint}"') IS NULL THEN RETURN; END IF;
                    IF NOT EXISTS (SELECT 1 FROM "{Checkpoint}" WHERE "TableName" = '{table}') THEN RETURN; END IF;
                END IF;
            END IF;
            """;
}

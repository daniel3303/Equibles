using Microsoft.EntityFrameworkCore.Migrations;

namespace Equibles.Migrations.Infrastructure;

// Frozen historical expansion; compatibility mirrors are retired after all writers switch.
internal static class NativeEquityEvidenceExpansion20260912
{
    public static void Apply(
        MigrationBuilder migration,
        string previousTable,
        string canonicalTable,
        string[] columns,
        string createSql
    )
    {
        var fields = string.Join(", ", columns.Select(Quote));
        var expectedColumns = string.Join(", ", columns.Append("CommonStockId").Select(Literal));
        migration.Sql(
            $"""
            SET LOCAL lock_timeout = '5s';
            {Shape(previousTable, expectedColumns)}
            {createSql}
            """
        );
        // FK creation briefly locks the issuer table; commit before scanning any evidence rows.
        migration.Sql("SELECT 1;", suppressTransaction: true);
        migration.Sql(
            $"""
            SET LOCAL lock_timeout = '5s';
            LOCK TABLE {Quote(previousTable)}, {Quote(canonicalTable)} IN SHARE ROW EXCLUSIVE MODE;
            {Shape(previousTable, expectedColumns)}
            DO $owners$ BEGIN
                IF EXISTS (SELECT 1 FROM {Quote(
                previousTable
            )} WHERE "CommonStockId" IS DISTINCT FROM "EquityIssuerId")
                    THEN RAISE EXCEPTION 'Original issuer owners disagree in %', {Literal(
                previousTable
            )}; END IF;
            END $owners$;
            INSERT INTO {Quote(canonicalTable)} ({fields})
                SELECT {fields} FROM {Quote(previousTable)} ON CONFLICT ("Id") DO NOTHING;
            {Verify(previousTable, canonicalTable, fields)}
            {Mirror(previousTable, canonicalTable, columns)}
            {Mirror(canonicalTable, previousTable, columns)}
            """
        );
        // Release this evidence table's write lock before expanding the next table.
        migration.Sql("SELECT 1;", suppressTransaction: true);
    }

    private static string Shape(string previousTable, string expectedColumns) =>
        $"""
            DO $shape$ BEGIN
                IF EXISTS (
                    (SELECT attname::text FROM pg_attribute WHERE attrelid = {Literal(
                Quote(previousTable)
            )}::regclass
                        AND attnum > 0 AND NOT attisdropped EXCEPT SELECT unnest(ARRAY[{expectedColumns}]))
                    UNION ALL
                    (SELECT unnest(ARRAY[{expectedColumns}]) EXCEPT SELECT attname::text FROM pg_attribute
                        WHERE attrelid = {Literal(
                Quote(previousTable)
            )}::regclass AND attnum > 0 AND NOT attisdropped)
                ) THEN RAISE EXCEPTION 'Unexpected original columns in %', {Literal(
                previousTable
            )}; END IF;
            END $shape$;
            """;

    private static string Verify(string previousTable, string canonicalTable, string fields) =>
        $"""
            DO $verify$ BEGIN
                IF EXISTS (
                    (SELECT {fields} FROM {Quote(
                previousTable
            )} EXCEPT ALL SELECT {fields} FROM {Quote(canonicalTable)})
                    UNION ALL
                    (SELECT {fields} FROM {Quote(
                canonicalTable
            )} EXCEPT ALL SELECT {fields} FROM {Quote(previousTable)})
                ) THEN RAISE EXCEPTION 'Original and canonical evidence differ in %', {Literal(
                previousTable
            )}; END IF;
            END $verify$;
            """;

    private static string Mirror(string source, string target, string[] columns)
    {
        var function = "eq_mirror_evidence_" + source.ToLowerInvariant();
        var fields = string.Join(", ", columns.Select(Quote));
        var values = string.Join(", ", columns.Select(column => "NEW." + Quote(column)));
        var assignments = string.Join(
            ", ",
            columns
                .Where(column => column != "Id")
                .Select(column => Quote(column) + " = EXCLUDED." + Quote(column))
        );
        var current = string.Join(", ", columns.Select(column => "destination." + Quote(column)));
        var incoming = string.Join(", ", columns.Select(column => "EXCLUDED." + Quote(column)));
        return $"""
            CREATE OR REPLACE FUNCTION {Quote(
                function
            )}() RETURNS trigger LANGUAGE plpgsql AS $mirror$
            BEGIN
                IF TG_OP = 'DELETE' THEN
                    DELETE FROM {Quote(target)} WHERE "Id" = OLD."Id";
                    RETURN OLD;
                END IF;
                IF TG_OP = 'UPDATE' THEN
                    IF OLD."Id" IS DISTINCT FROM NEW."Id" THEN
                        DELETE FROM {Quote(target)} WHERE "Id" = OLD."Id";
                    END IF;
                END IF;
                INSERT INTO {Quote(target)} AS destination ({fields}) VALUES ({values})
                    ON CONFLICT ("Id") DO UPDATE SET {assignments}
                    WHERE ROW({current}) IS DISTINCT FROM ROW({incoming});
                RETURN NEW;
            END $mirror$;
            CREATE OR REPLACE TRIGGER equity_evidence_mirror
                AFTER INSERT OR UPDATE OR DELETE ON {Quote(source)}
                FOR EACH ROW EXECUTE FUNCTION {Quote(function)}();
            """;
    }

    private static string Quote(string identifier) => '"' + identifier.Replace("\"", "\"\"") + '"';

    private static string Literal(string value) => '\'' + value.Replace("'", "''") + '\'';
}

using Microsoft.EntityFrameworkCore.Migrations;

namespace Equibles.Migrations.Infrastructure;

// Branch-only issuer migrations use this frozen metadata/validation boundary for live rollout.
internal static class NativeIssuerForeignKeyRetarget20260912
{
    public static void Apply(
        MigrationBuilder migration,
        string table,
        string column,
        string constraint,
        string previousConstraint = null
    )
    {
        var drop =
            previousConstraint == null
                ? ""
                : $"ALTER TABLE {Quote(table)} DROP CONSTRAINT IF EXISTS {Quote(previousConstraint)};";
        migration.Sql(
            $"""
            DO $retarget$ BEGIN
                SET LOCAL lock_timeout = '5s';
                {drop}
                IF NOT EXISTS (SELECT 1 FROM pg_constraint
                    WHERE conrelid = {Literal(Quote(table))}::regclass AND conname = {Literal(
                constraint
            )}) THEN
                    ALTER TABLE {Quote(table)} ADD CONSTRAINT {Quote(constraint)}
                        FOREIGN KEY ({Quote(
                column
            )}) REFERENCES "EquityIssuer"("Id") ON DELETE RESTRICT NOT VALID;
                END IF;
                IF NOT EXISTS (
                    SELECT 1 FROM pg_constraint constraint_row
                    JOIN pg_attribute owner ON owner.attrelid = constraint_row.conrelid AND owner.attname = {Literal(
                column
            )}
                    JOIN pg_attribute issuer ON issuer.attrelid = constraint_row.confrelid AND issuer.attname = 'Id'
                    WHERE constraint_row.conrelid = {Literal(Quote(table))}::regclass
                        AND constraint_row.conname = {Literal(
                constraint
            )} AND constraint_row.contype = 'f'
                        AND constraint_row.confrelid = '"EquityIssuer"'::regclass
                        AND constraint_row.conkey = ARRAY[owner.attnum] AND constraint_row.confkey = ARRAY[issuer.attnum]
                        AND constraint_row.confdeltype = 'r' AND constraint_row.confupdtype = 'a'
                        AND NOT constraint_row.condeferrable
                ) THEN RAISE EXCEPTION 'Unexpected issuer ownership constraint: %', {Literal(
                constraint
            )}; END IF;
            END $retarget$;
            """
        );
        // Commit all metadata locks before scanning existing records; ordinary reads/writes continue.
        migration.Sql(
            $"ALTER TABLE {Quote(table)} VALIDATE CONSTRAINT {Quote(constraint)};",
            suppressTransaction: true
        );
    }

    private static string Quote(string identifier) => '"' + identifier.Replace("\"", "\"\"") + '"';

    private static string Literal(string value) => '\'' + value.Replace("'", "''") + '\'';
}

using Equibles.Holdings.Data.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Equibles.TestSupport;

public static class HoldingsCorrectionRetirementProbe
{
    public static async Task Seed(DbContext db)
    {
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE public._backup_issue1535_holdings (
                "Id" uuid, "CommonStockId" uuid, "Shares" bigint, "Value" numeric,
                "CreationTime" timestamptz, "Note" text, "Ratio" double precision);
            INSERT INTO public._backup_issue1535_holdings VALUES
                ('11111111-1111-1111-1111-111111111111', '22222222-2222-2222-2222-222222222222',
                 9007199254740993, 12345678901234567890.12345678901234567890,
                 '2025-01-02T03:04:05.123456Z', E'Original\nΩ evidence', 0.12345678901234567),
                (NULL, NULL, NULL, NULL, NULL, NULL, NULL);
            INSERT INTO public._backup_issue1535_holdings SELECT * FROM public._backup_issue1535_holdings;
            CREATE TABLE public._backup_issue1535_manager_entries (
                "InstitutionalHoldingId" uuid, "Id" integer, "ManagerNumber" text,
                "Shares" numeric(30, 9), "Value" numeric, "InvestmentDiscretion" integer);
            INSERT INTO public._backup_issue1535_manager_entries VALUES
                ('11111111-1111-1111-1111-111111111111', 7, '00001', 9007199254740993.123456789,
                 98765432109876543210.98765432109876543210, NULL);
            """
        );
    }

    public static Task<string> OriginalRows(DbContext db) =>
        db
            .Database.SqlQueryRaw<string>(
                """
                SELECT jsonb_build_object(
                    '_backup_issue1535_holdings', (SELECT jsonb_agg(v ORDER BY v::text) FROM
                        (SELECT to_jsonb(r) v FROM public._backup_issue1535_holdings r) s),
                    '_backup_issue1535_manager_entries', (SELECT jsonb_agg(v ORDER BY v::text) FROM
                        (SELECT to_jsonb(r) v FROM public._backup_issue1535_manager_entries r) s))::text AS "Value"
                """
            )
            .SingleAsync();

    public static async Task AssertPreserved(DbContext db, string original)
    {
        var archived = await db
            .Database.SqlQueryRaw<string>(
                """
                SELECT jsonb_object_agg("SourceTable", rows)::text AS "Value" FROM (
                    SELECT "SourceTable", jsonb_agg("OriginalRow" ORDER BY "OriginalRow"::text) rows
                    FROM "HoldingsCorrectionEvidence" GROUP BY "SourceTable") s
                """
            )
            .SingleAsync();
        Require(archived == original, "Original correction fields or duplicate rows changed");
        Require(
            await db.Set<HoldingsCorrectionEvidence>().CountAsync() == 5,
            "Original evidence row count changed"
        );
        var schema = await db
            .Database.SqlQueryRaw<bool>(
                """
                SELECT bool_and("SourceSchema" @> jsonb_build_array(jsonb_build_object('name', 'Shares', 'type', 'numeric(30,9)'))) AS "Value"
                FROM "HoldingsCorrectionEvidence" WHERE "SourceTable" = '_backup_issue1535_manager_entries'
                """
            )
            .SingleAsync();
        Require(schema, "Original column precision was not retained");
        Require(
            await db
                .Database.SqlQueryRaw<bool>(
                    """
                    SELECT to_regclass('public._backup_issue1535_holdings') IS NULL
                       AND to_regclass('public._backup_issue1535_manager_entries') IS NULL AS "Value"
                    """
                )
                .SingleAsync(),
            "Ad hoc backup tables remain"
        );
    }

    public static async Task Run(DbContext db, string sql, string scenario)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();
        await Seed(db);
        var original = await OriginalRows(db);
        var start = sql.IndexOf(
            "-- Preserve historical correction evidence",
            StringComparison.Ordinal
        );
        var split = sql.IndexOf("DO $correction_evidence$", start, StringComparison.Ordinal);
        var end = sql.IndexOf("LOCK TABLE \"CommonStock\"", split, StringComparison.Ordinal);
        Require(start >= 0 && split > start && end > split, "Archive migration section is missing");
        await db.Database.ExecuteSqlRawAsync(
            "SET LOCAL timezone = 'UTC'; SET LOCAL extra_float_digits = 3;"
        );
        await transaction.CreateSavepointAsync("before_archive");
        await db.Database.ExecuteSqlRawAsync(sql[start..split]);
        if (
            scenario
            is "correction-corruption"
                or "correction-missing-field"
                or "correction-duplicate-loss"
        )
        {
            var change = scenario switch
            {
                "correction-corruption" =>
                    "NEW.\"OriginalRow\" := NEW.\"OriginalRow\" || jsonb_build_object('Shares', 1);",
                "correction-missing-field" =>
                    "NEW.\"OriginalRow\" := NEW.\"OriginalRow\" - 'CommonStockId';",
                _ =>
                    "IF EXISTS (SELECT 1 FROM \"HoldingsCorrectionEvidence\" WHERE \"OriginalRow\" = NEW.\"OriginalRow\") THEN RETURN NULL; END IF;",
            };
            await db.Database.ExecuteSqlRawAsync(
                "CREATE FUNCTION corrupt_correction_test() RETURNS trigger LANGUAGE plpgsql AS $test$ BEGIN "
                    + change
                    + " RETURN NEW; END $test$; CREATE TRIGGER corrupt_correction_test BEFORE INSERT ON \"HoldingsCorrectionEvidence\" FOR EACH ROW EXECUTE FUNCTION corrupt_correction_test();"
            );
        }
        if (scenario == "correction-dependent-view")
            await db.Database.ExecuteSqlRawAsync(
                "CREATE VIEW correction_dependency_test AS SELECT * FROM public._backup_issue1535_holdings;"
            );
        if (
            scenario
            is "correction-corruption"
                or "correction-missing-field"
                or "correction-duplicate-loss"
                or "correction-dependent-view"
        )
        {
            try
            {
                await db.Database.ExecuteSqlRawAsync(sql[split..end]);
                throw new InvalidOperationException("Incomplete correction archive was accepted");
            }
            catch (PostgresException error) when (error.SqlState is "P0001" or "2BP01")
            {
                await transaction.RollbackToSavepointAsync("before_archive");
            }
            Require(
                await OriginalRows(db) == original,
                "Refused retirement changed the original backups"
            );
            Require(
                await db
                    .Database.SqlQueryRaw<bool>(
                        "SELECT to_regclass('\"HoldingsCorrectionEvidence\"') IS NULL AS \"Value\""
                    )
                    .SingleAsync(),
                "Refused retirement left a partial audit table"
            );
            return;
        }
        await db.Database.ExecuteSqlRawAsync(sql[split..end]);
        await AssertPreserved(db, original);
        await transaction.CreateSavepointAsync("immutable_archive");
        var forbidden = scenario switch
        {
            "correction-update" =>
                "UPDATE \"HoldingsCorrectionEvidence\" SET \"OriginalRow\" = jsonb_build_object()",
            "correction-delete" => "DELETE FROM \"HoldingsCorrectionEvidence\"",
            "correction-truncate" => "TRUNCATE \"HoldingsCorrectionEvidence\"",
            _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
        };
        try
        {
            await db.Database.ExecuteSqlRawAsync(forbidden);
            throw new InvalidOperationException("Immutable correction evidence could be changed");
        }
        catch (PostgresException error) when (error.SqlState == "P0001")
        {
            await transaction.RollbackToSavepointAsync("immutable_archive");
        }
        await AssertPreserved(db, original);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}

using System.Data.Common;

namespace Equibles.TestSupport;

// Only for disposable test databases: restore the production guard before any test runs.
public static class ImmutableEvidenceTestReset
{
    public static async Task Run(DbConnection connection, Func<Task> reset)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            ALTER TABLE "EquityDirectorySourceRecord" DISABLE TRIGGER equity_directory_source_evidence_no_truncate;
            DO $reset$ BEGIN
                IF to_regclass('"HoldingsCorrectionEvidence"') IS NOT NULL THEN
                    ALTER TABLE "HoldingsCorrectionEvidence" DISABLE TRIGGER "TR_HoldingsCorrectionEvidence_Immutable";
                END IF;
            END $reset$;
            """;
        await command.ExecuteNonQueryAsync();
        try
        {
            await reset();
        }
        finally
        {
            command.CommandText = """
                ALTER TABLE "EquityDirectorySourceRecord" ENABLE TRIGGER equity_directory_source_evidence_no_truncate;
                DO $reset$ BEGIN
                    IF to_regclass('"HoldingsCorrectionEvidence"') IS NOT NULL THEN
                        ALTER TABLE "HoldingsCorrectionEvidence" ENABLE TRIGGER "TR_HoldingsCorrectionEvidence_Immutable";
                    END IF;
                END $reset$;
                """;
            await command.ExecuteNonQueryAsync();
        }
    }
}

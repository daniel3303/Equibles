using System.Data.Common;

namespace Equibles.TestSupport;

// Only for disposable test databases: restore the production guard before any test runs.
public static class ImmutableEvidenceTestReset
{
    public static async Task Run(DbConnection connection, Func<Task> reset)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """ALTER TABLE "EquityDirectorySourceRecord" DISABLE TRIGGER equity_directory_source_evidence_no_truncate;""";
        await command.ExecuteNonQueryAsync();
        try
        {
            await reset();
        }
        finally
        {
            command.CommandText =
                """ALTER TABLE "EquityDirectorySourceRecord" ENABLE TRIGGER equity_directory_source_evidence_no_truncate;""";
            await command.ExecuteNonQueryAsync();
        }
    }
}

using Equibles.IntegrationTests.Helpers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace Equibles.IntegrationTests.Holdings;

// Production's first deploy built the two repair worklists with predicates that name the retry
// stamp columns, which turns the repricing lane's daily stamp updates into index-maintaining
// updates. The follow-up migration must swap those for the model's predicates without touching a
// database that already carries them.
[Collection(ParadeDbCollection.Name)]
public class HoldingRepairWorklistMigrationTests(ParadeDbFixture fixture)
{
    private const string PreviousMigration =
        "20261009165133_AddHoldingQuarterRebuildAndRepairIndexes";
    private const string FiledRevise = "IX_InstitutionalHolding_FiledReviseRepair";
    private const string UnmarkedZero = "IX_InstitutionalHolding_UnmarkedZeroRepair";

    [Fact]
    public async Task ReplacesStampReferencingWorklistsAndLeavesTheModelsOnesAlone()
    {
        await using var context = fixture.CreateNativeDbContext();
        context.Database.SetCommandTimeout(TimeSpan.FromMinutes(5));
        var migrator = context.GetService<IMigrator>();

        // Rewinding the follow-up only forgets it (its Down is a no-op); then shape the worklists
        // the way the first published migration built them.
        await migrator.MigrateAsync(PreviousMigration);
        await context.Database.ExecuteSqlRawAsync(
            $"""
            DROP INDEX IF EXISTS "{FiledRevise}";
            DROP INDEX IF EXISTS "{UnmarkedZero}";
            CREATE INDEX "{FiledRevise}" ON "InstitutionalHolding" ("Id")
                WHERE NOT "ValuePending" AND "ShareType" = 0 AND NOT "ValueUnavailable"
                AND "ValueSource" = 1 AND "ValueLastRetryAt" IS NULL
                AND "FiledValue" IS NOT NULL AND "FiledValue" > 0
                AND "Value" = "FiledValue" AND "Shares" > 0;
            CREATE INDEX "{UnmarkedZero}" ON "InstitutionalHolding" ("Id")
                WHERE "Value" = 0 AND NOT "ValuePending" AND NOT "ValueUnavailable"
                AND ("FiledValue" IS NULL OR "FiledValue" <= 0) AND "ValueRetryCount" > 0;
            """
        );

        await migrator.MigrateAsync();

        var replaced = await RepairWorklists(context);
        replaced.Keys.Should().BeEquivalentTo([FiledRevise, UnmarkedZero]);
        replaced.Values.Should().OnlyContain(index => index.Valid);
        replaced[FiledRevise].Definition.Should().NotContain("ValueLastRetryAt");
        replaced[UnmarkedZero].Definition.Should().NotContain("ValueRetryCount");
        (await context.Database.GetPendingMigrationsAsync()).Should().BeEmpty();

        // Applying it against the model's predicates rebuilds nothing: same index OIDs.
        await migrator.MigrateAsync(PreviousMigration);
        await migrator.MigrateAsync();

        var untouched = await RepairWorklists(context);
        untouched.Should().BeEquivalentTo(replaced);
    }

    // Read the catalog with a plain command: the context's proxy conventions reject ad hoc types.
    private static async Task<
        Dictionary<string, (bool Valid, string Definition, uint Oid)>
    > RepairWorklists(DbContext context)
    {
        var connection = context.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT c.relname, i.indisvalid, pg_get_indexdef(c.oid), c.oid
            FROM pg_class c JOIN pg_index i ON i.indexrelid = c.oid
            WHERE c.relname LIKE 'IX\_InstitutionalHolding\_FiledReviseRepair%'
               OR c.relname LIKE 'IX\_InstitutionalHolding\_UnmarkedZeroRepair%'
            """;
        var rows = new Dictionary<string, (bool Valid, string Definition, uint Oid)>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            rows[reader.GetString(0)] = (
                reader.GetBoolean(1),
                reader.GetString(2),
                (uint)reader.GetValue(3)
            );
        return rows;
    }
}

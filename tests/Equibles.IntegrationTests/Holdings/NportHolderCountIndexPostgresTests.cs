using Equibles.IntegrationTests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Equibles.IntegrationTests.Holdings;

[Collection(ParadeDbCollection.Name)]
public class NportHolderCountIndexPostgresTests(ParadeDbFixture fixture)
{
    [Fact]
    public async Task EquityIdentityCountUsesTheCoveringIndex()
    {
        await using var db = fixture.CreateDbContext();
        await db.Database.OpenConnectionAsync();
        var definition = await db
            .Database.SqlQueryRaw<string>(
                """
                SELECT pg_get_indexdef(c.oid) AS "Value"
                FROM pg_class c JOIN pg_index i ON i.indexrelid = c.oid
                WHERE c.relname = 'IX_NportHolding_EquityIdentity' AND i.indisvalid
                """
            )
            .SingleAsync();
        definition.Should().Contain("(\"Isin\", \"Cusip\", \"Lei\", \"NportFilingId\")");

        await using var transaction = await db.Database.BeginTransactionAsync();
        // The migrated test table is empty; make PostgreSQL cost an indexed plan so this
        // verifies that the partial predicate and count can actually use the covering index.
        await db.Database.ExecuteSqlRawAsync("SET LOCAL enable_seqscan = off");
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText = """
            EXPLAIN (COSTS OFF)
            SELECT "Isin", "Cusip", "Lei", count(DISTINCT "NportFilingId")
            FROM "NportHolding"
            WHERE "AssetCategory" = 'EC' AND "Isin" IS NOT NULL
              AND "Isin" = ANY (ARRAY['JE00BV7DQ550', 'JE00B783TY65'])
            GROUP BY "Isin", "Cusip", "Lei"
            """;
        await using var reader = await command.ExecuteReaderAsync();
        var plan = new List<string>();
        while (await reader.ReadAsync())
            plan.Add(reader.GetString(0));
        string.Join('\n', plan)
            .Should()
            .Contain("Index Only Scan using \"IX_NportHolding_EquityIdentity\"");
    }

    [Fact]
    public async Task MigratedDatabase_CoversCusipAndLatestFilingJoin()
    {
        await using var db = fixture.CreateDbContext();
        var definition = await db
            .Database.SqlQueryRaw<string>(
                """
                SELECT pg_get_indexdef(c.oid) AS "Value"
                FROM pg_class c JOIN pg_index i ON i.indexrelid = c.oid
                WHERE c.relname = 'IX_NportHolding_CusipFiling' AND i.indisvalid
                """
            )
            .SingleAsync();
        definition.Should().Contain("(\"Cusip\") INCLUDE (\"NportFilingId\")");
        var oldIndex = await db
            .Database.SqlQueryRaw<int>(
                """
                SELECT count(*)::int AS "Value" FROM pg_class
                WHERE relname = 'IX_NportHolding_Cusip'
                """
            )
            .SingleAsync();
        oldIndex.Should().Be(0);
    }
}

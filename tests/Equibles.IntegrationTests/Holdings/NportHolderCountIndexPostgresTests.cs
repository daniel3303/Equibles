using Equibles.IntegrationTests.Helpers;
using Microsoft.EntityFrameworkCore;

namespace Equibles.IntegrationTests.Holdings;

[Collection(ParadeDbCollection.Name)]
public class NportHolderCountIndexPostgresTests(ParadeDbFixture fixture)
{
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

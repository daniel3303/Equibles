using Equibles.IntegrationTests.Helpers;
using Microsoft.EntityFrameworkCore;

namespace Equibles.IntegrationTests.Holdings;

[Collection(ParadeDbCollection.Name)]
public class HoldingsStockQuarterIndexPostgresTests(ParadeDbFixture fixture)
{
    [Fact]
    public async Task MigratedDatabase_HasValidIndexCoveringStockQuarterFiltersAndAggregates()
    {
        await using var db = fixture.CreateDbContext();
        var definition = await db
            .Database.SqlQueryRaw<string>(
                """
                SELECT pg_get_indexdef(c.oid) AS "Value"
                FROM pg_class c JOIN pg_index i ON i.indexrelid = c.oid
                WHERE c.relname = 'IX_InstitutionalHolding_StockQuarterExposure' AND i.indisvalid
                """
            )
            .SingleAsync();
        definition
            .Should()
            .Contain(
                "(\"EquityIssuerId\", \"ReportDate\") INCLUDE (\"InstitutionalHolderId\", \"Value\", \"Shares\", \"ListedTicker\", \"FilingType\", \"OptionType\")"
            );
        var oldIndex = await db
            .Database.SqlQueryRaw<int>(
                """
                SELECT count(*)::int AS "Value" FROM pg_class
                WHERE relname = 'IX_InstitutionalHolding_CommonStockId_ReportDate'
                """
            )
            .SingleAsync();
        oldIndex.Should().Be(0);
    }
}

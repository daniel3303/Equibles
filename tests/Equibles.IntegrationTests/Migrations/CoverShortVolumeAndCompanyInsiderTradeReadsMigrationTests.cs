using Equibles.IntegrationTests.Helpers;
using Npgsql;
using Xunit;

namespace Equibles.IntegrationTests.Migrations;

/// <summary>
/// The migration builds both covering indexes in raw SQL, so the scaffolded model and the live
/// schema agree only if that SQL ran: each table must carry the covering index, keep the
/// short-volume uniqueness, and no longer carry the key-only index it replaced.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class CoverShortVolumeAndCompanyInsiderTradeReadsMigrationTests(ParadeDbFixture fixture)
{
    [Fact]
    public async Task ShortVolume_KeepsItsUniqueListingDateKeyAndCoversTheVolumes()
    {
        var indexes = await Indexes("DailyShortVolume");

        Assert.DoesNotContain(indexes, i => i.Name == "IX_DailyShortVolume_EquityListingId_Date");
        var covering = Assert.Single(
            indexes,
            i => i.Name == "IX_DailyShortVolume_EquityListingId_Date_Covering"
        );
        Assert.True(covering.IsValid);
        Assert.Equal(
            "CREATE UNIQUE INDEX \"IX_DailyShortVolume_EquityListingId_Date_Covering\" "
                + "ON public.\"DailyShortVolume\" USING btree (\"EquityListingId\", \"Date\") "
                + "INCLUDE (\"ShortVolume\", \"ShortExemptVolume\", \"TotalVolume\")",
            covering.Definition
        );
    }

    [Fact]
    public async Task InsiderTrades_ReplaceTheCompanyDateIndexWithTheCoveringOne()
    {
        var indexes = await Indexes("InsiderTransaction");

        Assert.DoesNotContain(
            indexes,
            i => i.Name == "IX_InsiderTransaction_EquityIssuerId_TransactionDate"
        );
        var covering = Assert.Single(
            indexes,
            i => i.Name == "IX_InsiderTransaction_EquityIssuerId_TransactionDate_Covering"
        );
        Assert.True(covering.IsValid);
        Assert.Equal(
            "CREATE INDEX \"IX_InsiderTransaction_EquityIssuerId_TransactionDate_Covering\" "
                + "ON public.\"InsiderTransaction\" USING btree (\"EquityIssuerId\", \"TransactionDate\") "
                + "INCLUDE (\"FilingDate\", \"TransactionCode\", \"Shares\", \"PricePerShare\", "
                + "\"InsiderOwnerId\", \"IsRule10b5One\", \"IsPriceValid\", \"SecurityKind\", \"SecurityTitle\")",
            covering.Definition
        );
    }

    private async Task<List<(string Name, bool IsValid, string Definition)>> Indexes(string table)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT c.relname, i.indisvalid, pg_get_indexdef(i.indexrelid) "
                + "FROM pg_index i JOIN pg_class c ON c.oid = i.indexrelid "
                + "WHERE i.indrelid = format('%I', @table)::regclass",
            connection
        );
        command.Parameters.AddWithValue("table", table);

        var rows = new List<(string, bool, string)>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            rows.Add((reader.GetString(0), reader.GetBoolean(1), reader.GetString(2)));
        return rows;
    }
}

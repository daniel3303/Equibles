using Equibles.IntegrationTests.Helpers;
using Npgsql;
using Xunit;

namespace Equibles.IntegrationTests.Migrations;

/// <summary>
/// Pins the one step of PerListing13FIndexSwap nothing else exercises: the rename. The old
/// six-column and new seven-column holding unique indexes truncate to the SAME 63-char name,
/// so the migration builds the replacement under a temporary name and renames it onto the EF
/// name after the old index drops. Every functional test passes whether or not the rename
/// happened — Postgres infers the ON CONFLICT arbiter from the column tuple, not the index
/// name — so a silently skipped rename (or a leftover temp index) would only surface as a
/// failure in some LATER migration or scaffold. Assert the end state directly against
/// pg_index after the fixture's MigrateAsync. Plain Npgsql rather than an EF ad-hoc query:
/// the context runs with proxies, which reject ad-hoc result types.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class PerListing13FIndexSwapMigrationTests
{
    private readonly ParadeDbFixture _fixture;

    public PerListing13FIndexSwapMigrationTests(ParadeDbFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task HoldingUniqueIndex_CarriesEfNameWithSevenKeyColumnsAndNoTempLeftover()
    {
        await using var connection = new NpgsqlConnection(_fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT c.relname, i.indisunique, i.indisvalid, i.indnullsnotdistinct, i.indnkeyatts::int "
                + "FROM pg_class c "
                + "JOIN pg_index i ON i.indexrelid = c.oid "
                + "JOIN pg_class t ON t.oid = i.indrelid "
                + "WHERE t.relname = 'InstitutionalHolding' AND c.relkind = 'i' "
                + "AND c.relnamespace = 'public'::regnamespace",
            connection
        );

        var rows = new List<(string Name, bool IsUnique, bool IsValid, bool NullsNotDistinct, int KeyColumnCount)>();
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                rows.Add(
                    (
                        reader.GetString(0),
                        reader.GetBoolean(1),
                        reader.GetBoolean(2),
                        reader.GetBoolean(3),
                        reader.GetInt32(4)
                    )
                );
            }
        }

        var efName = "IX_InstitutionalHolding_CommonStockId_InstitutionalHolderId_Re~";
        var renamed = Assert.Single(rows, r => r.Name == efName);
        Assert.True(renamed.IsUnique);
        Assert.True(renamed.IsValid);
        Assert.True(renamed.NullsNotDistinct);
        Assert.Equal(7, renamed.KeyColumnCount);

        Assert.DoesNotContain(rows, r => r.Name == "IX_InstitutionalHolding_HoldingKeyPerListing");
    }
}

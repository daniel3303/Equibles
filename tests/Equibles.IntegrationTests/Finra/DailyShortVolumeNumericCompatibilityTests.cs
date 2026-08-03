using Equibles.CommonStocks.Data.Models;
using Equibles.Finra.Data.Models;
using Equibles.IntegrationTests.Helpers;
using Npgsql;

namespace Equibles.IntegrationTests.Finra;

[Collection(ParadeDbCollection.Name)]
public class DailyShortVolumeNumericCompatibilityTests : ParadeDbMcpTestBase
{
    public DailyShortVolumeNumericCompatibilityTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task IntegralNumericVolume_CanBeReadByPreMigrationLongConsumer()
    {
        var stock = new CommonStock
        {
            Cik = "0000000778",
            Ticker = "NUMERIC",
            Name = "Numeric Compatibility Inc.",
        };
        DbContext.Add(stock);
        DbContext.Add(
            new DailyShortVolume
            {
                CommonStockId = stock.Id,
                Date = new DateOnly(2026, 8, 1),
                ShortVolume = long.MaxValue,
                ShortExemptVolume = 42,
                TotalVolume = 9_007_199_254_740_991,
            }
        );
        await DbContext.SaveChangesAsync();

        await using var connection = new NpgsqlConnection(Fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT "ShortVolume", "ShortExemptVolume", "TotalVolume"
            FROM "DailyShortVolume"
            WHERE "CommonStockId" = @stockId
            """;
        command.Parameters.AddWithValue("stockId", stock.Id);

        await using var reader = await command.ExecuteReaderAsync();
        (await reader.ReadAsync()).Should().BeTrue();
        reader.GetFieldValue<long>(0).Should().Be(long.MaxValue);
        reader.GetFieldValue<long>(1).Should().Be(42);
        reader.GetFieldValue<long>(2).Should().Be(9_007_199_254_740_991);
    }
}

using Equibles.CommonStocks.Data.Models;
using Equibles.Data;
using Equibles.IntegrationTests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Equibles.IntegrationTests.CommonStocks;

[Collection(ParadeDbCollection.Name)]
public class NativeListingFinraTests(ParadeDbFixture fixture) : ParadeDbMcpTestBase(fixture)
{
    private static readonly string[] Tables =
    [
        "DailyShortVolume",
        "ShortInterest",
        "OffExchangeVolume",
    ];

    private EquiblesFinancialDbContext _migrationContext;
    private EquiblesFinancialDbContext Context => _migrationContext ?? DbContext;

    [Fact]
    public async Task Migration_PreservesEveryObservationAndPartitionField_WithoutLegacyOwner()
    {
        await using var database = await IsolatedMigrationDatabase.Create(
            Fixture,
            "20260912021338_RetargetFailsToDeliverToListings"
        );
        _migrationContext = database.Context;
        var stock = new CommonStock
        {
            Ticker = "FIRA",
            Name = "Original issuer",
            Cik = "0000000093",
            SecondaryTickers = ["FIRB"],
        };
        Context.Add(stock);
        await Context.SaveChangesAsync();
        foreach (var table in Tables)
        {
            await Insert(table, stock.Id, "FIRA", false);
            await Insert(table, stock.Id, "FIRB", false);
        }
        await Context.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO "FinraImportPartition" ("Dataset", "PartitionDate", "ScopeKey", "ImportedAt")
            VALUES ('daily-short-volume-files-v3', DATE '2026-08-03', 'listings:original', now())
            """
        );
        var before = await Snapshot();
        await ApplyMigration();
        (await Snapshot()).Should().Be(before);
        foreach (var table in Tables)
        {
            var count = await Context
                .Database.SqlQueryRaw<int>(
                    $"""
                    SELECT count(*)::int AS "Value" FROM "{table}" observation
                    JOIN "LegacyEquityListing" mapping ON mapping."EquityListingId" = observation."EquityListingId"
                    WHERE mapping."CommonStockId" = observation."CommonStockId" AND mapping."ListedTicker" = observation."ListedTicker"
                    """
                )
                .SingleAsync();
            count.Should().Be(2);
        }
        await Context.Database.ExecuteSqlInterpolatedAsync(
            $"""DELETE FROM "CommonStock" WHERE "Id" = {stock.Id}"""
        );
        (await Snapshot()).Should().Be(before);
    }

    [Theory]
    [InlineData("DailyShortVolume")]
    [InlineData("ShortInterest")]
    [InlineData("OffExchangeVolume")]
    public async Task Migration_UnattributedHistoryAbortsWithoutLosingRows(string table)
    {
        await using var database = await IsolatedMigrationDatabase.Create(
            Fixture,
            "20260912021338_RetargetFailsToDeliverToListings"
        );
        _migrationContext = database.Context;
        var stock = new CommonStock
        {
            Ticker = "FIRU",
            Name = "Unknown source",
            Cik = "0000000093",
        };
        Context.Add(stock);
        await Context.SaveChangesAsync();
        await Insert(table, stock.Id, "", false);
        var before = await Snapshot();
        Func<Task> apply = ApplyMigration;
        await apply
            .Should()
            .ThrowAsync<PostgresException>()
            .Where(error => error.MessageText.Contains("unresolved listing identities"));
        (await Snapshot()).Should().Be(before);
    }

    [Theory]
    [InlineData("DailyShortVolume")]
    [InlineData("ShortInterest")]
    [InlineData("OffExchangeVolume")]
    public async Task NativeWriter_KeepsSameTickerVenuesDistinct_AndRefusesListingDeletion(
        string table
    )
    {
        await using var transaction = await Context.Database.BeginTransactionAsync();
        var issuer = new EquityIssuer { Name = "Native issuer" };
        var security = new EquitySecurity { Issuer = issuer };
        var first = new EquityListing
        {
            Security = security,
            Ticker = "SAME",
            MarketIdentifierCode = "XNYS",
        };
        var second = new EquityListing
        {
            Security = security,
            Ticker = "SAME",
            MarketIdentifierCode = "XLIS",
        };
        Context.AddRange(first, second);
        await Context.SaveChangesAsync();
        await Insert(table, first.Id, "SAME", true);
        await Insert(table, second.Id, "SAME", true);
        var before = await Snapshot();
        (
            await Context
                .Database.SqlQueryRaw<int>(
                    $"""SELECT count(*)::int AS "Value" FROM "{table}" WHERE "CommonStockId" IS NULL"""
                )
                .SingleAsync()
        )
            .Should()
            .Be(2);
        (await Context.Set<CommonStock>().CountAsync()).Should().Be(0);
        await transaction.CreateSavepointAsync("before_delete");
        Func<Task> delete = async () =>
            await Context
                .Set<EquityListing>()
                .Where(row => row.Id == first.Id)
                .ExecuteDeleteAsync();
        await delete
            .Should()
            .ThrowAsync<PostgresException>()
            .Where(error => error.SqlState == PostgresErrorCodes.RestrictViolation);
        await transaction.RollbackToSavepointAsync("before_delete");
        (await Snapshot()).Should().Be(before);
    }

    [Theory]
    [InlineData("DailyShortVolume")]
    [InlineData("ShortInterest")]
    [InlineData("OffExchangeVolume")]
    public async Task RetiringWriter_ResolvesExactSecondaryListing(string table)
    {
        await using var transaction = await Context.Database.BeginTransactionAsync();
        var stock = new CommonStock
        {
            Ticker = "FIRA",
            Name = "Rolling issuer",
            Cik = "0000000093",
            SecondaryTickers = ["FIRB"],
        };
        Context.Add(stock);
        await Context.SaveChangesAsync();
        await Insert(table, stock.Id, "FIRB", false);
        var listingId = await Context
            .Set<LegacyEquityListing>()
            .Where(row => row.CommonStockId == stock.Id && row.ListedTicker == "FIRB")
            .Select(row => row.EquityListingId)
            .SingleAsync();
        (
            await Context
                .Database.SqlQueryRaw<Guid>(
                    $"""SELECT "EquityListingId" AS "Value" FROM "{table}" """
                )
                .SingleAsync()
        )
            .Should()
            .Be(listingId);
    }

    [Fact]
    public async Task Verification_AcceptsRetainedFormerSymbolsOnMappedNativeListings()
    {
        await using var transaction = await Context.Database.BeginTransactionAsync();
        var stock = new CommonStock
        {
            Ticker = "CURRENT",
            Name = "Renamed issuer",
            Cik = "0000000093",
        };
        Context.Add(stock);
        await Context.SaveChangesAsync();
        var listingId = await Context
            .Set<LegacyEquityListing>()
            .Where(row => row.CommonStockId == stock.Id)
            .Select(row => row.EquityListingId)
            .SingleAsync();
        foreach (var table in Tables)
            await Insert(table, listingId, "FORMER", true);
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (
            directory != null
            && !File.Exists(
                Path.Combine(directory.FullName, "scripts", "verify-native-finra-listings.sql")
            )
        )
            directory = directory.Parent;
        directory.Should().NotBeNull();
        var sql = await File.ReadAllTextAsync(
            Path.Combine(directory.FullName, "scripts", "verify-native-finra-listings.sql")
        );
        await using var command = Context.Database.GetDbConnection().CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText = sql.Replace("BEGIN READ ONLY;", "").Replace("COMMIT;", "");
        await using var reader = await command.ExecuteReaderAsync();
        var verifiedTables = 0;
        do
        {
            while (await reader.ReadAsync())
            {
                for (var field = 0; field < reader.FieldCount; field++)
                    if (
                        reader.GetName(field)
                        is "missing_listings"
                            or "mismatched_original_issuers"
                            or "mismatched_original_listings"
                            or "duplicate_listing_dates"
                    )
                        reader.GetInt64(field).Should().Be(0);
                if (reader.GetName(0) == "observation_table")
                    verifiedTables++;
            }
        } while (await reader.NextResultAsync());
        verifiedTables.Should().Be(3);
    }

    private Task<int> Insert(string table, Guid owner, string ticker, bool native)
    {
        if (!Tables.Contains(table))
            throw new ArgumentException(nameof(table));
        var identity = native ? "EquityListingId" : "CommonStockId";
        var fields = table switch
        {
            "DailyShortVolume" =>
                "\"Date\", \"ShortVolume\", \"ShortExemptVolume\", \"TotalVolume\", \"Market\"",
            "ShortInterest" =>
                "\"SettlementDate\", \"CurrentShortPosition\", \"PreviousShortPosition\", \"ChangeInShortPosition\", \"AverageDailyVolume\", \"DaysToCover\"",
            _ =>
                "\"WeekStartDate\", \"AtsVolume\", \"AtsTradeCount\", \"NonAtsOtcVolume\", \"NonAtsOtcTradeCount\"",
        };
        var values = table switch
        {
            "DailyShortVolume" =>
                "DATE '2026-08-03', 9223372036854775807.123456, 789.654321, 9999999999999999999.999999, 'B,N,Q'",
            "ShortInterest" =>
                "DATE '2026-08-03', 9223372036854775807, 9223372036854775000, 807, 987654321, 123.45",
            _ => "DATE '2026-08-03', 9223372036854775807, 123456789, 456789012, 789123456",
        };
        return Context.Database.ExecuteSqlRawAsync(
            $"""
            INSERT INTO "{table}" ("Id", "{identity}", "ListedTicker", "CreationTime", {fields})
            VALUES (@id, @owner, @ticker, TIMESTAMPTZ '2026-08-04 01:02:03.123456Z', {values})
            """,
            new NpgsqlParameter("id", Guid.NewGuid()),
            new NpgsqlParameter("owner", owner),
            new NpgsqlParameter("ticker", ticker)
        );
    }

    private Task ApplyMigration() => Context.Database.MigrateAsync();

    private async Task<string> Snapshot()
    {
        var snapshots = new List<string>();
        foreach (var table in Tables.Append("FinraImportPartition"))
            snapshots.Add(
                await Context
                    .Database.SqlQueryRaw<string>(
                        $"""
                        SELECT coalesce(jsonb_agg(to_jsonb(row) - 'EquityListingId' ORDER BY to_jsonb(row))::text, '[]') AS "Value" FROM "{table}" row
                        """
                    )
                    .SingleAsync()
            );
        return string.Join('\n', snapshots);
    }
}

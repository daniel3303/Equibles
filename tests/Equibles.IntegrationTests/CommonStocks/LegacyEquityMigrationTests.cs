using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Data;
using Equibles.Finra.Data.Models;
using Equibles.Holdings.Data.Models;
using Equibles.IntegrationTests.Helpers;
using Equibles.Yahoo.Data.Models;
using Equibles.Yahoo.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;

namespace Equibles.IntegrationTests.CommonStocks;

[Collection(ParadeDbCollection.Name)]
public class LegacyEquityMigrationTests : ParadeDbMcpTestBase
{
    public LegacyEquityMigrationTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task PopulatedDatabaseMigration_PreservesEveryLegacyValue_AndMapsHistoricalSeries()
    {
        var database = "identity_" + Guid.NewGuid().ToString("N");
        await using var admin = new NpgsqlConnection(Fixture.ConnectionString);
        await admin.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE DATABASE \"{database}\"", admin))
            await create.ExecuteNonQueryAsync();
        try
        {
            var connection = new NpgsqlConnectionStringBuilder(Fixture.ConnectionString)
            {
                Database = database,
            }.ConnectionString;
            await using var context = Fixture.CreateDbContext(options =>
                options.UseNpgsql(connection)
            );
            await context
                .GetService<IMigrator>()
                .MigrateAsync("20260911164403_AddEquityIdentityFoundation");
            var stock = new CommonStock
            {
                Ticker = "CURRENT",
                Name = "Original name",
                SecondaryTickers = ["CLASS-B"],
            };
            var inactive = new CommonStock
            {
                Ticker = "DEAD",
                Name = null,
                Active = false,
            };
            context.AddRange(stock, inactive);
            context.AddRange(
                Price(stock, "CURRENT", 1),
                Price(stock, "CLASS-B", 2),
                Price(stock, "OLD", 3),
                Price(inactive, "DEAD", 4)
            );
            context.Add(Activity(stock, "ACTIVITY-OLD"));
            context.Add(ShortVolume(stock, "SHORT-OLD", 1));
            context.Add(ShortVolume(stock, "", 2));
            await context.SaveChangesAsync();
            var before = await LegacySnapshot(context);
            await context.Database.MigrateAsync();
            (await LegacySnapshot(context)).Should().Be(before);
            await context.Database.ExecuteSqlRawAsync(AuditSql());
            (await context.Set<EquityIssuer>().CountAsync()).Should().Be(2);
            (await context.Set<LegacyEquityListing>().CountAsync()).Should().Be(6);
            (await context.Set<LegacyEquityListing>().AnyAsync(row => row.ListedTicker == ""))
                .Should()
                .BeFalse();
            (
                await new EquityListingRepository(context)
                    .GetByLegacyKey(stock.Id, "ACTIVITY-OLD")
                    .CountAsync()
            )
                .Should()
                .Be(1);
            (
                await new EquityListingRepository(context)
                    .GetByLegacyKey(stock.Id, "SHORT-OLD")
                    .CountAsync()
            )
                .Should()
                .Be(1);
            var listings = await context.Set<EquityListing>().ToListAsync();
            listings
                .Should()
                .OnlyContain(row =>
                    row.IdentityState == EquityIdentityState.Legacy
                    && row.MarketIdentifierCode == null
                    && row.TradingCurrency == null
                    && row.QuoteUnitMultiplier == null
                );
            var old = await new EquityListingRepository(context)
                .GetByLegacyKey(stock.Id, "OLD")
                .SingleAsync();
            (await new DailyStockPriceRepository(context).GetByListing(old.Id).SingleAsync())
                .Close.Should()
                .Be(123.4567m);
            (await new CommonStockRepository(context).GetByTicker("CLASS-B"))
                .Id.Should()
                .Be(stock.Id);
            (await context.Set<CommonStock>().SingleAsync(row => row.Id == inactive.Id))
                .Active.Should()
                .BeFalse();
        }
        finally
        {
            await using var drop = new NpgsqlCommand(
                $"DROP DATABASE \"{database}\" WITH (FORCE)",
                admin
            );
            await drop.ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public async Task NewAndRenamedSymbols_KeepStableHistory_WithoutInventingShareClasses()
    {
        var stock = new CommonStock
        {
            Ticker = "OLD",
            Name = "Issuer",
            SecondaryTickers = ["CLASS"],
        };
        DbContext.Add(stock);
        DbContext.Add(Price(stock, "OLD", 1));
        await DbContext.SaveChangesAsync();
        var old = await new EquityListingRepository(DbContext)
            .GetByLegacyKey(stock.Id, "OLD")
            .SingleAsync();
        stock.Ticker = "NEW";
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        var retained = await new EquityListingRepository(DbContext)
            .GetByLegacyKey(stock.Id, "OLD")
            .SingleAsync();
        retained.Id.Should().Be(old.Id);
        retained.Active.Should().BeFalse();
        (await new DailyStockPriceRepository(DbContext).GetByListing(old.Id).CountAsync())
            .Should()
            .Be(1);
        (await DbContext.Set<EquitySecurity>().ToListAsync())
            .Should()
            .OnlyContain(row => row.SecurityType == EquitySecurityKind.Unknown);
        (
            await new EquityListingRepository(DbContext)
                .GetVerifiedByMarket("XLIS", "NEW")
                .CountAsync()
        )
            .Should()
            .Be(0);
    }

    [Fact]
    public async Task ConcurrentHistoricalWriters_CreateOneMapping_AndKeepBothBars()
    {
        var stock = new CommonStock { Ticker = "CURRENT" };
        DbContext.Add(stock);
        await DbContext.SaveChangesAsync();
        async Task Write(int day)
        {
            await using var context = Fixture.CreateDbContext();
            var price = Price(stock, "HISTORICAL", day);
            price.CommonStock = null;
            context.Add(price);
            await context.SaveChangesAsync();
        }
        await Task.WhenAll(Write(1), Write(2));
        var listing = await new EquityListingRepository(DbContext)
            .GetByLegacyKey(stock.Id, "HISTORICAL")
            .SingleAsync();
        (await new DailyStockPriceRepository(DbContext).GetByListing(listing.Id).CountAsync())
            .Should()
            .Be(2);
        (await DbContext.Set<EquitySecurity>().CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task VerifiedListing_RequiresCurrencyVenueAndQuoteScale()
    {
        var stock = new CommonStock { Ticker = "SAME" };
        DbContext.Add(stock);
        await DbContext.SaveChangesAsync();
        var listing = await new EquityListingRepository(DbContext)
            .GetByLegacyKey(stock.Id, "SAME")
            .SingleAsync();
        listing.IdentityState = EquityIdentityState.Verified;
        Func<Task> save = () => DbContext.SaveChangesAsync();
        await save.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task CompletionAudit_RejectsAMissingMapping()
    {
        var stock = new CommonStock { Ticker = "AUDIT" };
        DbContext.Add(stock);
        await DbContext.SaveChangesAsync();
        await DbContext
            .Set<LegacyEquityListing>()
            .Where(row => row.CommonStockId == stock.Id)
            .ExecuteDeleteAsync();
        Func<Task> audit = () => DbContext.Database.ExecuteSqlRawAsync(AuditSql());
        await audit
            .Should()
            .ThrowAsync<PostgresException>()
            .WithMessage("*unmapped directory symbols*");
    }

    [Fact]
    public async Task ActivityAndShortDataWriters_MapExactSeries_AndRetainUnattributedRows()
    {
        var stock = new CommonStock { Ticker = "CURRENT" };
        DbContext.Add(stock);
        await DbContext.SaveChangesAsync();
        DbContext.Add(Activity(stock, "ACTIVITY-OLD"));
        DbContext.Add(ShortVolume(stock, "", 1));
        DbContext.Add(ShortVolume(stock, "SHORT-OLD", 2));
        await DbContext.SaveChangesAsync();
        (await DbContext.Set<LegacyEquityListing>().Select(row => row.ListedTicker).ToListAsync())
            .Should()
            .BeEquivalentTo("CURRENT", "ACTIVITY-OLD", "SHORT-OLD");
        (await DbContext.Set<DailyShortVolume>().SingleAsync(row => row.ListedTicker == ""))
            .ShortVolume.Should()
            .Be(123.456789m);
        await DbContext.Database.ExecuteSqlRawAsync(AuditSql());
        await DbContext
            .Set<LegacyEquityListing>()
            .Where(row => row.ListedTicker == "ACTIVITY-OLD")
            .ExecuteDeleteAsync();
        Func<Task> audit = () => DbContext.Database.ExecuteSqlRawAsync(AuditSql());
        await audit
            .Should()
            .ThrowAsync<PostgresException>()
            .WithMessage("*StockQuarterlyListingActivity has 1 unmapped series*");
    }

    private static StockQuarterlyListingActivity Activity(CommonStock stock, string ticker) =>
        new()
        {
            CommonStockId = stock.Id,
            ReportDate = new DateOnly(2025, 3, 31),
            PriceSeriesTicker = ticker,
            CurrentShares = 123456,
            PreviousShares = 654321,
        };

    private static DailyShortVolume ShortVolume(CommonStock stock, string ticker, int day) =>
        new()
        {
            CommonStockId = stock.Id,
            ListedTicker = ticker,
            Date = new DateOnly(2025, 1, day),
            ShortVolume = 123.456789m,
            TotalVolume = 234.567890m,
        };

    private static string AuditSql()
    {
        for (
            var root = new DirectoryInfo(AppContext.BaseDirectory);
            root != null;
            root = root.Parent
        )
        {
            var file = Path.Combine(root.FullName, "scripts", "verify-equity-identity.sql");
            if (File.Exists(file))
                return File.ReadAllText(file);
        }
        throw new FileNotFoundException("Equity identity completion audit missing");
    }

    private static DailyStockPrice Price(CommonStock stock, string ticker, int day) =>
        new()
        {
            CommonStock = stock,
            CommonStockId = stock.Id,
            ListedTicker = ticker,
            Date = new DateOnly(2025, 1, day),
            Open = 120m,
            High = 125m,
            Low = 119m,
            Close = 123.4567m,
            AdjustedClose = 111.1111m,
            Volume = 12345,
        };

    private static async Task<string> LegacySnapshot(EquiblesFinancialDbContext context)
    {
        await context.Database.OpenConnectionAsync();
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            SELECT (SELECT jsonb_agg(to_jsonb(s) ORDER BY "Id")::text FROM "CommonStock" s)
              || (SELECT jsonb_agg(to_jsonb(p) ORDER BY "Id")::text FROM "ListedDailyStockPrice" p)
              || (SELECT jsonb_agg(to_jsonb(a) ORDER BY "PriceSeriesTicker")::text FROM "StockQuarterlyListingActivity" a)
              || (SELECT jsonb_agg(to_jsonb(v) ORDER BY "Id")::text FROM "DailyShortVolume" v)
            """;
        return (string)await command.ExecuteScalarAsync();
    }
}

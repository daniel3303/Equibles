using System.Data.Common;
using Equibles.CommonStocks.Data.Models;
using Equibles.Data;
using Equibles.Finra.Data.Models;
using Equibles.Finra.Repositories;
using Equibles.IntegrationTests.Helpers;
using Equibles.Sec.Data.Models;
using Equibles.Sec.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using Xunit;

namespace Equibles.IntegrationTests.Finra;

/// <summary>
/// Proves the planner's choice for a latest-N read of one exact listing: it must walk the
/// (EquityListingId, date) index backward and stop, never read and sort the listing's history.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class ListingHistoryIndexPlanTests(ParadeDbFixture fixture) : IAsyncLifetime
{
    private const int Periods = 1_500;
    private static readonly DateOnly First = new(1990, 1, 1);

    public Task InitializeAsync() => fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Theory]
    [InlineData(nameof(DailyShortVolume))]
    [InlineData(nameof(ShortInterest))]
    [InlineData(nameof(OffExchangeVolume))]
    [InlineData(nameof(FailToDeliver))]
    public async Task LatestRead_WalksTheListingDateIndexBackward_WithoutSorting(string table)
    {
        var issuer = await Seed(table);
        var capture = new CommandCapture();
        await using var context = fixture.CreateDbContext(options =>
            options.AddInterceptors(capture)
        );

        var latest = await ReadLatest(table, context, issuer);

        latest.Should().Be(First.AddDays(Periods - 1));
        var plan = await Explain(capture.Commands.Single(c => c.Text.Contains($"\"{table}\"")));
        plan.Should()
            .MatchRegex($"Index (Only )?Scan Backward using \"IX_{table}_EquityListingId_")
            .And.NotContain("Sort");
    }

    private async Task<EquityIssuer> Seed(string table)
    {
        await using var db = fixture.CreateDbContext();
        EquityIssuer target = null;
        foreach (var ticker in new[] { "TEST", "FILL" })
        {
            var issuer = Equibles.TestSupport.EquityIssuerSeed.Create(
                Id: Guid.NewGuid(),
                Ticker: ticker,
                Name: ticker
            );
            db.Add(issuer);
            var listing = Equibles.TestSupport.NativeListingSeed.ForStock(db, issuer, ticker);
            for (var period = 0; period < Periods; period++)
                db.Add(Row(table, listing.Id, ticker, First.AddDays(period)));
            target ??= issuer;
        }
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlRawAsync($"ANALYZE \"{table}\"");
        return target;
    }

    private static object Row(string table, Guid listingId, string ticker, DateOnly date) =>
        table switch
        {
            nameof(DailyShortVolume) => new DailyShortVolume
            {
                EquityListingId = listingId,
                ListedTicker = ticker,
                Date = date,
                Market = "TRF",
            },
            nameof(ShortInterest) => new ShortInterest
            {
                EquityListingId = listingId,
                ListedTicker = ticker,
                SettlementDate = date,
            },
            nameof(OffExchangeVolume) => new OffExchangeVolume
            {
                EquityListingId = listingId,
                ListedTicker = ticker,
                WeekStartDate = date,
            },
            _ => new FailToDeliver
            {
                EquityListingId = listingId,
                ListedTicker = ticker,
                SettlementDate = date,
            },
        };

    private static async Task<DateOnly> ReadLatest(
        string table,
        EquiblesFinancialDbContext db,
        EquityIssuer issuer
    ) =>
        table switch
        {
            nameof(DailyShortVolume) => (
                await new DailyShortVolumeRepository(db)
                    .GetHistoryByListing(issuer, "TEST")
                    .OrderByDescending(row => row.Date)
                    .Take(1)
                    .ToListAsync()
            )
                .Single()
                .Date,
            nameof(ShortInterest) => (
                await new ShortInterestRepository(db)
                    .GetHistoryByListing(issuer, "TEST")
                    .OrderByDescending(row => row.SettlementDate)
                    .Take(1)
                    .ToListAsync()
            )
                .Single()
                .SettlementDate,
            nameof(OffExchangeVolume) => (
                await new OffExchangeVolumeRepository(db)
                    .GetHistoryByListing(issuer, "TEST")
                    .OrderByDescending(row => row.WeekStartDate)
                    .Take(1)
                    .ToListAsync()
            )
                .Single()
                .WeekStartDate,
            _ => (
                await new FailToDeliverRepository(db)
                    .GetByListing(issuer, "TEST")
                    .OrderByDescending(row => row.SettlementDate)
                    .Take(1)
                    .ToListAsync()
            )
                .Single()
                .SettlementDate,
        };

    private async Task<string> Explain(CapturedCommand command)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var explain = new NpgsqlCommand("EXPLAIN " + command.Text, connection);
        foreach (var parameter in command.Parameters)
            explain.Parameters.Add(parameter.Clone());
        await using var reader = await explain.ExecuteReaderAsync();
        var lines = new List<string>();
        while (await reader.ReadAsync())
            lines.Add(reader.GetString(0));
        return string.Join('\n', lines);
    }

    private sealed record CapturedCommand(string Text, List<NpgsqlParameter> Parameters);

    private sealed class CommandCapture : DbCommandInterceptor
    {
        public List<CapturedCommand> Commands { get; } = [];

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default
        )
        {
            Commands.Add(
                new CapturedCommand(
                    command.CommandText,
                    command.Parameters.Cast<NpgsqlParameter>().Select(p => p.Clone()).ToList()
                )
            );
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}

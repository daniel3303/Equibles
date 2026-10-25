using System.Data.Common;
using Equibles.CommonStocks.Data.Models;
using Equibles.IntegrationTests.Helpers;
using Equibles.TestSupport;
using Equibles.Yahoo.Data.Models;
using Equibles.Yahoo.HostedService.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using Xunit;

namespace Equibles.IntegrationTests.Yahoo;

/// <summary>
/// Proves the migrated covering index answers the 13F valuation's close windows index-only: bars
/// are stored in date order, so on the live table every heap fetch is a random read of its own.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class YahooStockPriceProviderIndexPlanTests(ParadeDbFixture fixture) : IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task ClosingPriceWindow_ScansTheCoveringIndexOnly()
    {
        var issuers = Enumerable
            .Range(0, 300)
            .Select(index => EquityIssuerSeed.Create(Ticker: $"T{index}", Name: $"Issuer {index}"))
            .ToList();
        var firstDay = new DateOnly(2026, 1, 1);
        await using (var db = fixture.CreateDbContext())
        {
            db.AddRange(issuers);
            var listings = issuers
                .Select(issuer => NativeListingSeed.ForStock(db, issuer))
                .ToList();
            db.AddRange(
                // Day by day across every listing, as the daily import writes them, so one
                // listing's bars spread across the heap.
                from day in Enumerable.Range(0, 120)
                from listing in listings
                select new EquityDailyStockPrice
                {
                    Listing = listing,
                    SourceTicker = listing.Ticker,
                    Date = firstDay.AddDays(day),
                    Close = 10m + day,
                    Volume = 1_000,
                }
            );
            await db.SaveChangesAsync();
        }
        await using (var connection = new NpgsqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync();
            // Index-only scans need the visibility map a vacuum sets, as on the settled table.
            // Testcontainers runs Postgres with synchronous_commit off, so the checkpoint flushes
            // the seed's commit first or VACUUM cannot mark its pages all-visible.
            await using var checkpoint = new NpgsqlCommand("CHECKPOINT;", connection);
            await checkpoint.ExecuteNonQueryAsync();
            await using var vacuum = new NpgsqlCommand(
                "VACUUM (ANALYZE) \"EquityDailyStockPrice\";",
                connection
            );
            await vacuum.ExecuteNonQueryAsync();
        }
        var capture = new CommandCapture();
        await using var context = fixture.CreateDbContext(options =>
            options.AddInterceptors(capture)
        );
        var date = firstDay.AddDays(90);

        var prices = await new YahooStockPriceProvider(context).GetClosingPrices(
            issuers.Take(20).Select(issuer => (issuer.Id, (string)null, date))
        );

        prices.Should().HaveCount(20).And.OnlyContain(price => price.Value == 100m);
        var window = capture.Commands.Single(command =>
            command.Text.Contains("FROM \"EquityDailyStockPrice\"")
        );
        var plan = await Explain(window);
        plan.Should()
            .Contain("Index Only Scan using \"IX_EquityDailyStockPrice_ListingSeries\"")
            .And.NotContain("Seq Scan on \"EquityDailyStockPrice\"");
    }

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

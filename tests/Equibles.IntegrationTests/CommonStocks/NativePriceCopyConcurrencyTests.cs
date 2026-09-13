using Equibles.CommonStocks.Data.Models;
using Equibles.IntegrationTests.Helpers;
using Equibles.Migrations.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Npgsql;

namespace Equibles.IntegrationTests.CommonStocks;

[Collection(HistoricalEquityDbCollection.Name)]
public class NativePriceCopyConcurrencyTests(HistoricalEquityDbFixture fixture)
{
    [Fact]
    public async Task CopyLocksOnlySelectedSourceRowsAndMirrorsCorrectionsAfterCommit()
    {
        await using var database = await IsolatedMigrationDatabase.Create(
            fixture,
            "20260911213842_PopulateNativeEquityProfiles"
        );
        var context = database.Context;
        var stock = new CommonStock { Ticker = "LOCKED" };
        context.Add(stock);
        await context.SaveChangesAsync();
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO "DailyStockPrice" ("Id", "CommonStockId", "Date", "Open", "High", "Low", "Close", "AdjustedClose", "Volume", "CreationTime")
            SELECT ('00000000-0000-0000-0000-' || lpad(i::text, 12, '0'))::uuid,
                {stock.Id}, DATE '2020-01-01' + i, 1.2345, 3.4567, 0.9876, 2.3456, 1.8765,
                9876543210 + i, TIMESTAMPTZ '2026-08-04 01:02:03.123456Z'
            FROM generate_series(1, 2) i;
            """
        );
        await context.Database.ExecuteSqlRawAsync(
            new PopulateNativeEquityPrices().UpOperations.Cast<SqlOperation>().First().Sql
        );
        await context.Database.ExecuteSqlRawAsync(
            """
            CREATE FUNCTION pause_price_checkpoint() RETURNS trigger LANGUAGE plpgsql AS $test$
            BEGIN
                IF NEW."TableName" = 'DailyStockPrice' AND NEW."CopiedRows" > 0 THEN
                    PERFORM pg_advisory_xact_lock(178901);
                END IF;
                RETURN NEW;
            END $test$;
            CREATE TRIGGER pause_price_checkpoint BEFORE UPDATE ON "NativePriceMigrationProgress"
                FOR EACH ROW EXECUTE FUNCTION pause_price_checkpoint();
            """
        );
        await using var blocker = new NpgsqlConnection(database.ConnectionString);
        await using var writer = new NpgsqlConnection(database.ConnectionString);
        await blocker.OpenAsync();
        await writer.OpenAsync();
        await Execute(blocker, "SELECT pg_advisory_lock(178901)");
        var migration = context
            .GetService<IMigrator>()
            .MigrateAsync("20260913025100_PreserveHoldingObservationIdentity");
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(50));
            while (!await CheckpointIsWaiting(writer))
                await timer.WaitForNextTickAsync(deadline.Token);
            await Execute(writer, "SET lock_timeout = '250ms'");
            Func<Task> update = () =>
                Execute(
                    writer,
                    """
                    UPDATE "DailyStockPrice" SET "Close" = 2.9876 WHERE "Id" = '00000000-0000-0000-0000-000000000001';
                    """
                );
            (await update.Should().ThrowAsync<PostgresException>())
                .Which.SqlState.Should()
                .Be(PostgresErrorCodes.LockNotAvailable);
            await using var insert = new NpgsqlCommand(
                """
                INSERT INTO "DailyStockPrice" ("Id", "CommonStockId", "Date", "Open", "High", "Low", "Close", "AdjustedClose", "Volume", "CreationTime")
                VALUES ('00000000-0000-0000-0000-000000000000', @owner, DATE '1999-01-01',
                    1.2345, 3.4567, 0.9876, 2.3456, 1.8765, 9876543210, now());
                """,
                writer
            );
            insert.Parameters.AddWithValue("owner", stock.Id);
            await insert.ExecuteNonQueryAsync();
        }
        finally
        {
            await Execute(blocker, "SELECT pg_advisory_unlock(178901)");
            await migration;
        }
        await Execute(
            writer,
            """
            UPDATE "DailyStockPrice" SET "Close" = 2.9876 WHERE "Id" = '00000000-0000-0000-0000-000000000001';
            DELETE FROM "DailyStockPrice" WHERE "Id" = '00000000-0000-0000-0000-000000000002';
            """
        );
        (
            await context
                .Database.SqlQueryRaw<decimal>(
                    """
                    SELECT "Close" AS "Value" FROM "UnattributedDailyStockPrice" WHERE "Id" = '00000000-0000-0000-0000-000000000001'
                    """
                )
                .SingleAsync()
        ).Should().Be(2.9876m);
        (
            await context
                .Database.SqlQueryRaw<long>(
                    """SELECT count(*) AS "Value" FROM "UnattributedDailyStockPrice" """
                )
                .SingleAsync()
        )
            .Should()
            .Be(2);
        (
            await context
                .Database.SqlQueryRaw<long>(
                    """
                    SELECT count(*) AS "Value" FROM (
                        (SELECT "Id", "Date", "Open", "High", "Low", "Close", "AdjustedClose", "Volume", "CreationTime", "CommonStockId" FROM "DailyStockPrice"
                            EXCEPT ALL SELECT "Id", "Date", "Open", "High", "Low", "Close", "AdjustedClose", "Volume", "CreationTime", "EquityIssuerId" FROM "UnattributedDailyStockPrice")
                        UNION ALL
                        (SELECT "Id", "Date", "Open", "High", "Low", "Close", "AdjustedClose", "Volume", "CreationTime", "EquityIssuerId" FROM "UnattributedDailyStockPrice"
                            EXCEPT ALL SELECT "Id", "Date", "Open", "High", "Low", "Close", "AdjustedClose", "Volume", "CreationTime", "CommonStockId" FROM "DailyStockPrice")
                    ) differences
                    """
                )
                .SingleAsync()
        ).Should().Be(0);
    }

    private static async Task<bool> CheckpointIsWaiting(NpgsqlConnection connection)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT EXISTS (SELECT 1 FROM pg_locks WHERE locktype = 'advisory' AND objid = 178901 AND NOT granted
                AND database = (SELECT oid FROM pg_database WHERE datname = current_database()))
            """,
            connection
        );
        return (bool)(await command.ExecuteScalarAsync());
    }

    private static async Task Execute(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection) { CommandTimeout = 30 };
        await command.ExecuteNonQueryAsync();
    }
}

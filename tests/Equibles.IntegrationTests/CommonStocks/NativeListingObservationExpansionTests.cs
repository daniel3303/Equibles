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
public class NativeListingObservationExpansionTests(HistoricalEquityDbFixture fixture)
{
    [Fact]
    public async Task InterruptedBatchResumesWithoutChangingOriginalFieldsAndAcceptsRetiringWrites()
    {
        await using var database = await IsolatedMigrationDatabase.Create(
            fixture,
            "20260912003738_RetargetIssuerDisclosures"
        );
        var context = database.Context;
        var stock = new CommonStock
        {
            Ticker = "BATCH",
            Name = "Original — ação",
            Cik = "0000000093",
        };
        context.Add(stock);
        await context.SaveChangesAsync();
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO "FailToDeliver" ("Id", "CommonStockId", "ListedTicker", "SettlementDate", "Quantity", "Price", "CreationTime")
            SELECT ('00000000-0000-0000-0000-' || lpad(i::text, 12, '0'))::uuid,
                {stock.Id}, 'BATCH', DATE '2000-01-01' + i, 9223372036854775807 - i,
                123456789.123456789 + i, TIMESTAMPTZ '2026-08-04 01:02:03.123456Z'
            FROM generate_series(1, 20003) i;
            CREATE TABLE "OriginalObservations" AS SELECT to_jsonb(observation) AS payload FROM "FailToDeliver" observation;
            """
        );
        var operations = new RetargetFailsToDeliverToListings()
            .UpOperations.Cast<SqlOperation>()
            .ToArray();
        // Install the real expansion before injecting a failure at the second batch's checkpoint.
        foreach (
            var operation in operations.TakeWhile(operation =>
                !operation.Sql.Contains("DO $backfill$")
            )
        )
            await context.Database.ExecuteSqlRawAsync(operation.Sql);
        await context.Database.ExecuteSqlRawAsync(
            """
            CREATE FUNCTION stop_second_observation_batch() RETURNS trigger LANGUAGE plpgsql AS $test$
            BEGIN
                IF NEW."UpdatedRows" > 10000 THEN RAISE EXCEPTION 'Injected second batch failure'; END IF;
                RETURN NEW;
            END $test$;
            CREATE TRIGGER stop_second_batch BEFORE UPDATE ON "NativeListingObservationMigrationProgress"
                FOR EACH ROW EXECUTE FUNCTION stop_second_observation_batch();
            """
        );
        Func<Task> migrate = () =>
            context
                .GetService<IMigrator>()
                .MigrateAsync("20260913025100_PreserveHoldingObservationIdentity");
        (await migrate.Should().ThrowAsync<PostgresException>())
            .Which.MessageText.Should()
            .Be("Injected second batch failure");
        (
            await context
                .Database.SqlQueryRaw<long>(
                    """
                    SELECT count(*) AS "Value" FROM "FailToDeliver" WHERE "EquityListingId" IS NOT NULL
                    """
                )
                .SingleAsync()
        ).Should().Be(10000);
        (
            await context
                .Database.SqlQueryRaw<long>(
                    """
                    SELECT "UpdatedRows" AS "Value" FROM "NativeListingObservationMigrationProgress" WHERE "TableName" = 'FailToDeliver'
                    """
                )
                .SingleAsync()
        ).Should().Be(10000);
        await AssertOriginalFields(context);

        await using (var writer = new NpgsqlConnection(database.ConnectionString))
        {
            await writer.OpenAsync();
            await using var command = new NpgsqlCommand(
                """
                SET lock_timeout = '1s';
                INSERT INTO "FailToDeliver" ("Id", "CommonStockId", "ListedTicker", "SettlementDate", "Quantity", "Price", "CreationTime")
                VALUES ('00000000-0000-0000-0000-000000000000', @owner, 'BATCH', DATE '1999-01-01',
                    321, 987.123456789, TIMESTAMPTZ '2026-08-04 01:02:03.123456Z');
                INSERT INTO "OriginalObservations" SELECT to_jsonb(observation) - 'EquityListingId'
                    FROM "FailToDeliver" observation WHERE "Id" = '00000000-0000-0000-0000-000000000000';
                """,
                writer
            );
            command.Parameters.AddWithValue("owner", stock.Id);
            await command.ExecuteNonQueryAsync();
        }
        await context.Database.ExecuteSqlRawAsync(
            """
            DROP TRIGGER stop_second_batch ON "NativeListingObservationMigrationProgress";
            DROP FUNCTION stop_second_observation_batch();
            """
        );
        await migrate();
        await AssertOriginalFields(context);
        (
            await context
                .Database.SqlQueryRaw<long>(
                    """
                    SELECT count(*) AS "Value" FROM "FailToDeliver" WHERE "EquityListingId" IS NOT NULL
                    """
                )
                .SingleAsync()
        ).Should().Be(20004);
        (
            await context
                .Database.SqlQueryRaw<bool>(
                    """
                    SELECT "Completed" AS "Value" FROM "NativeListingObservationMigrationProgress" WHERE "TableName" = 'FailToDeliver'
                    """
                )
                .SingleAsync()
        ).Should().BeTrue();

        var indexId = await IndexId(context);
        foreach (var operation in operations)
            await context.Database.ExecuteSqlRawAsync(operation.Sql);
        (await IndexId(context)).Should().Be(indexId);
        await AssertOriginalFields(context);

        await context.Database.ExecuteSqlRawAsync(
            """DROP INDEX "IX_FailToDeliver_EquityListingId_SettlementDate";"""
        );
        Func<Task> interruptedIndex = () =>
            context.Database.ExecuteSqlRawAsync(
                """
                CREATE UNIQUE INDEX CONCURRENTLY "IX_FailToDeliver_EquityListingId_SettlementDate" ON "FailToDeliver" ("ListedTicker");
                """
            );
        (await interruptedIndex.Should().ThrowAsync<PostgresException>())
            .Which.SqlState.Should()
            .Be(PostgresErrorCodes.UniqueViolation);
        foreach (var operation in operations)
            await context.Database.ExecuteSqlRawAsync(operation.Sql);
        (
            await context
                .Database.SqlQueryRaw<bool>(
                    """
                    SELECT indisvalid AS "Value" FROM pg_index
                        WHERE indexrelid = '"IX_FailToDeliver_EquityListingId_SettlementDate"'::regclass
                    """
                )
                .SingleAsync()
        ).Should().BeTrue();
        await AssertOriginalFields(context);
    }

    private static Task<long> IndexId(Equibles.Data.EquiblesFinancialDbContext context) =>
        context
            .Database.SqlQueryRaw<long>(
                """
                SELECT '"IX_FailToDeliver_EquityListingId_SettlementDate"'::regclass::oid::bigint AS "Value"
                """
            )
            .SingleAsync();

    private static async Task AssertOriginalFields(Equibles.Data.EquiblesFinancialDbContext context)
    {
        (
            await context
                .Database.SqlQueryRaw<long>(
                    """
                    SELECT count(*) AS "Value" FROM (
                        (SELECT payload FROM "OriginalObservations"
                            EXCEPT ALL SELECT to_jsonb(observation) - 'EquityListingId' FROM "FailToDeliver" observation)
                        UNION ALL
                        (SELECT to_jsonb(observation) - 'EquityListingId' FROM "FailToDeliver" observation
                            EXCEPT ALL SELECT payload FROM "OriginalObservations")
                    ) differences
                    """
                )
                .SingleAsync()
        ).Should().Be(0);
    }
}

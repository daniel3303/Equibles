using Equibles.CommonStocks.Data.Models;
using Equibles.Data;
using Equibles.IntegrationTests.Helpers;
using Equibles.Migrations.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Npgsql;

namespace Equibles.IntegrationTests.CommonStocks;

[Collection(ParadeDbCollection.Name)]
public class NativeEquityPriceExpansionTests(ParadeDbFixture fixture)
{
    [Theory]
    [InlineData("DailyStockPrice", "UnattributedDailyStockPrice")]
    [InlineData("ListedDailyStockPrice", "EquityDailyStockPrice")]
    public async Task InterruptedCopyPreservesBothOriginalStoresAndSurvivesDirectoryRetirement(
        string source,
        string target
    )
    {
        await using var database = await IsolatedMigrationDatabase.Create(
            fixture,
            "20260911213842_PopulateNativeEquityProfiles"
        );
        var context = database.Context;
        var stock = new CommonStock
        {
            Ticker = "BATCH",
            Name = "Original — ação",
            Cik = "0000000093",
        };
        var writerStock = new CommonStock
        {
            Ticker = "WRITER",
            Name = "Concurrent issuer",
            Cik = "0000000094",
        };
        context.AddRange(stock, writerStock);
        await context.SaveChangesAsync();
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO "DailyStockPrice" ("Id", "CommonStockId", "Date", "Open", "High", "Low", "Close", "AdjustedClose", "Volume", "CreationTime")
            SELECT ('00000000-0000-0000-0000-' || lpad(i::text, 12, '0'))::uuid,
                {stock.Id}, DATE '2000-01-01' + i, 1.2345, 3.4567, 0.9876, 2.3456, 1.8765,
                9223372036854775807 - i, TIMESTAMPTZ '2026-08-04 01:02:03.123456Z'
            FROM generate_series(1, 20003) i;
            INSERT INTO "ListedDailyStockPrice" ("Id", "CommonStockId", "ListedTicker", "Date", "Open", "High", "Low", "Close", "AdjustedClose", "Volume", "CreationTime")
            SELECT "Id", "CommonStockId", 'BATCH', "Date", "Open" + 10, "High" + 10, "Low" + 10,
                "Close" + 10, "AdjustedClose" + 10, "Volume" - 100, "CreationTime" + interval '1 microsecond'
            FROM "DailyStockPrice";
            CREATE TABLE "OriginalUnattributedPrices" AS SELECT to_jsonb(price) AS payload FROM "DailyStockPrice" price;
            CREATE TABLE "OriginalExactPrices" AS SELECT to_jsonb(price) AS payload FROM "ListedDailyStockPrice" price;
            """
        );
        var operations = new PopulateNativeEquityPrices()
            .UpOperations.Cast<SqlOperation>()
            .ToArray();
        await context.Database.ExecuteSqlRawAsync(operations[0].Sql);
        await context.Database.ExecuteSqlRawAsync(
            $"""
            CREATE FUNCTION stop_second_price_batch() RETURNS trigger LANGUAGE plpgsql AS $test$
            BEGIN
                IF NEW."TableName" = '{source}' AND NEW."CopiedRows" > 10000 THEN
                    RAISE EXCEPTION 'Injected second price batch failure';
                END IF;
                RETURN NEW;
            END $test$;
            CREATE TRIGGER stop_second_batch BEFORE UPDATE ON "NativePriceMigrationProgress"
                FOR EACH ROW EXECUTE FUNCTION stop_second_price_batch();
            """
        );
        Func<Task> migrate = () => context.Database.MigrateAsync();
        (await migrate.Should().ThrowAsync<PostgresException>())
            .Which.MessageText.Should()
            .Be("Injected second price batch failure");
        (
            await context
                .Database.SqlQueryRaw<long>($"""SELECT count(*) AS "Value" FROM "{target}" """)
                .SingleAsync()
        )
            .Should()
            .Be(10000);
        await AssertOriginalStores(context);
        context.ChangeTracker.Clear();
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"""DELETE FROM "CommonStock" WHERE "Id" = {stock.Id}"""
        );
        await AssertOriginalStores(context);

        await using (var writer = new NpgsqlConnection(database.ConnectionString))
        {
            await writer.OpenAsync();
            await using var command = new NpgsqlCommand(
                """
                SET lock_timeout = '1s';
                INSERT INTO "DailyStockPrice" ("Id", "CommonStockId", "Date", "Open", "High", "Low", "Close", "AdjustedClose", "Volume", "CreationTime")
                VALUES ('00000000-0000-0000-0000-000000000000', @owner, DATE '1999-01-01', 1.2345, 3.4567, 0.9876,
                    2.3456, 1.8765, 9876543210, TIMESTAMPTZ '2026-08-04 01:02:03.123456Z');
                INSERT INTO "ListedDailyStockPrice" ("Id", "CommonStockId", "ListedTicker", "Date", "Open", "High", "Low", "Close", "AdjustedClose", "Volume", "CreationTime")
                SELECT "Id", "CommonStockId", 'WRITER', "Date", "Open" + 10, "High" + 10, "Low" + 10,
                    "Close" + 10, "AdjustedClose" + 10, "Volume" - 100, "CreationTime" + interval '1 microsecond'
                FROM "DailyStockPrice" WHERE "Id" = '00000000-0000-0000-0000-000000000000';
                INSERT INTO "OriginalUnattributedPrices" SELECT to_jsonb(price) FROM "DailyStockPrice" price WHERE "Id" = '00000000-0000-0000-0000-000000000000';
                INSERT INTO "OriginalExactPrices" SELECT to_jsonb(price) FROM "ListedDailyStockPrice" price WHERE "Id" = '00000000-0000-0000-0000-000000000000';
                """,
                writer
            );
            command.Parameters.AddWithValue("owner", writerStock.Id);
            await command.ExecuteNonQueryAsync();
        }
        await context.Database.ExecuteSqlRawAsync(
            """
            DROP TRIGGER stop_second_batch ON "NativePriceMigrationProgress";
            DROP FUNCTION stop_second_price_batch();
            """
        );
        await migrate();
        await AssertOriginalStores(context);
        await AssertNativeCopies(context);
        (
            await context
                .Database.SqlQueryRaw<int>(
                    """SELECT count(*)::int AS "Value" FROM "NativePriceMigrationProgress" WHERE "Completed" """
                )
                .SingleAsync()
        )
            .Should()
            .Be(2);
        foreach (var operation in operations)
            await context.Database.ExecuteSqlRawAsync(operation.Sql);
        await AssertOriginalStores(context);
        await AssertNativeCopies(context);
    }

    [Fact]
    public async Task UnexpectedOriginalColumnRefusesCopyAndPreservesItsContents()
    {
        await using var database = await IsolatedMigrationDatabase.Create(
            fixture,
            "20260911213842_PopulateNativeEquityProfiles"
        );
        var context = database.Context;
        var stock = new CommonStock { Ticker = "EXTRA" };
        context.Add(stock);
        await context.SaveChangesAsync();
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            ALTER TABLE "DailyStockPrice" ADD COLUMN "OriginalEvidence" text;
            INSERT INTO "DailyStockPrice" ("Id", "CommonStockId", "Date", "Open", "High", "Low", "Close", "AdjustedClose", "Volume", "CreationTime", "OriginalEvidence")
            VALUES ({Guid.NewGuid()}, {stock.Id}, DATE '2020-01-02', 1, 3, 1, 2, 2, 123, now(), 'Fonte — original');
            """
        );
        Func<Task> migrate = () => context.Database.MigrateAsync();
        (await migrate.Should().ThrowAsync<PostgresException>())
            .Which.MessageText.Should()
            .Contain("unexpected original fields");
        (
            await context
                .Database.SqlQueryRaw<string>(
                    """SELECT "OriginalEvidence" AS "Value" FROM "DailyStockPrice" """
                )
                .SingleAsync()
        )
            .Should()
            .Be("Fonte — original");
        (
            await context
                .Database.SqlQueryRaw<bool>(
                    """SELECT to_regclass('"EquityDailyStockPrice"') IS NULL AS "Value" """
                )
                .SingleAsync()
        )
            .Should()
            .BeTrue();
    }

    private static async Task AssertOriginalStores(EquiblesFinancialDbContext context)
    {
        foreach (
            var (source, original) in new[]
            {
                ("DailyStockPrice", "OriginalUnattributedPrices"),
                ("ListedDailyStockPrice", "OriginalExactPrices"),
            }
        )
            (
                await context
                    .Database.SqlQueryRaw<long>(
                        $"""
                        SELECT count(*) AS "Value" FROM (
                            (SELECT payload FROM "{original}" EXCEPT ALL SELECT to_jsonb(price) FROM "{source}" price)
                            UNION ALL
                            (SELECT to_jsonb(price) FROM "{source}" price EXCEPT ALL SELECT payload FROM "{original}")
                        ) differences
                        """
                    )
                    .SingleAsync()
            ).Should().Be(0);
    }

    private static async Task AssertNativeCopies(EquiblesFinancialDbContext context)
    {
        (
            await context
                .Database.SqlQueryRaw<long>(
                    """
                    SELECT count(*) AS "Value" FROM (
                        (SELECT payload FROM "OriginalUnattributedPrices" EXCEPT ALL
                            SELECT (to_jsonb(price) - 'EquityIssuerId') || jsonb_build_object('CommonStockId', price."EquityIssuerId") FROM "UnattributedDailyStockPrice" price)
                        UNION ALL
                        (SELECT (to_jsonb(price) - 'EquityIssuerId') || jsonb_build_object('CommonStockId', price."EquityIssuerId") FROM "UnattributedDailyStockPrice" price
                            EXCEPT ALL SELECT payload FROM "OriginalUnattributedPrices")
                        UNION ALL
                        (SELECT payload FROM "OriginalExactPrices" EXCEPT ALL
                            SELECT (to_jsonb(price) - 'EquityListingId' - 'SourceTicker') || jsonb_build_object('CommonStockId', mapping."CommonStockId", 'ListedTicker', price."SourceTicker")
                            FROM "EquityDailyStockPrice" price JOIN "LegacyEquityListing" mapping ON mapping."EquityListingId" = price."EquityListingId")
                        UNION ALL
                        (SELECT (to_jsonb(price) - 'EquityListingId' - 'SourceTicker') || jsonb_build_object('CommonStockId', mapping."CommonStockId", 'ListedTicker', price."SourceTicker")
                            FROM "EquityDailyStockPrice" price JOIN "LegacyEquityListing" mapping ON mapping."EquityListingId" = price."EquityListingId"
                            EXCEPT ALL SELECT payload FROM "OriginalExactPrices")
                    ) differences
                    """
                )
                .SingleAsync()
        ).Should().Be(0);
    }
}

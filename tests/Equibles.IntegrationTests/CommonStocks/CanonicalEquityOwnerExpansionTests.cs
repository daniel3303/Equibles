using Equibles.CommonStocks.Data.Models;
using Equibles.CorporateActions.Data.Models;
using Equibles.IntegrationTests.Helpers;
using Equibles.Migrations.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Npgsql;

namespace Equibles.IntegrationTests.CommonStocks;

[Collection(ParadeDbCollection.Name)]
public class CanonicalEquityOwnerExpansionTests(ParadeDbFixture fixture)
{
    [Fact]
    public async Task InterruptedBackfillResumesWithoutChangingAnyOriginalFactField()
    {
        await WithIsolatedSchema(async connection =>
        {
            await Execute(
                connection,
                """
                CREATE TABLE "EquityIssuer" ("Id" uuid PRIMARY KEY);
                INSERT INTO "EquityIssuer" VALUES ('10000000-0000-0000-0000-000000000001'), ('10000000-0000-0000-0000-000000000002');
                CREATE TABLE "FinancialFact" (LIKE public."FinancialFact" INCLUDING ALL);
                ALTER TABLE "FinancialFact" DROP COLUMN "EquityIssuerId" CASCADE;
                ALTER TABLE "FinancialFact" ADD CONSTRAINT "original_owner" FOREIGN KEY ("CommonStockId") REFERENCES "EquityIssuer"("Id");
                INSERT INTO "FinancialFact" ("Id", "CommonStockId", "FinancialConceptId", "Unit", "PeriodType", "PeriodStart", "PeriodEnd",
                    "Value", "FiscalYear", "FiscalPeriod", "Form", "FiledDate", "AccessionNumber", "DimensionsKey", "CreationTime")
                SELECT ('00000000-0000-0000-0000-' || lpad(i::text, 12, '0'))::uuid,
                    '10000000-0000-0000-0000-000000000001', '20000000-0000-0000-0000-000000000001', 'EUR', 1,
                    '2025-01-01', '2025-12-31', -123456789012345.123456789 - i, 2025, 0, 'TwentyF', '2026-03-15',
                    i::text, 'dimensão', '2026-03-16T01:02:03.123456Z' FROM generate_series(1, 20003) i;
                CREATE TABLE "OriginalFactRows" AS SELECT to_jsonb(f) AS payload FROM "FinancialFact" f;
                CREATE FUNCTION stop_second_batch() RETURNS trigger LANGUAGE plpgsql AS $fail$
                BEGIN
                    IF NEW."Id" >= '00000000-0000-0000-0000-000000010001'::uuid THEN
                        RAISE EXCEPTION 'simulated interruption';
                    END IF;
                    RETURN NEW;
                END $fail$;
                CREATE TRIGGER "99_stop_second_batch" BEFORE UPDATE ON "FinancialFact"
                    FOR EACH ROW EXECUTE FUNCTION stop_second_batch();
                """
            );
            var interrupted = () => Apply(connection, "FinancialFact");
            await interrupted
                .Should()
                .ThrowAsync<PostgresException>()
                .WithMessage("*simulated interruption*");
            (
                await Scalar<long>(
                    connection,
                    """SELECT count(*) FROM "FinancialFact" WHERE "EquityIssuerId" IS NOT NULL"""
                )
            )
                .Should()
                .Be(10000);
            (
                await Scalar<long>(
                    connection,
                    """SELECT "UpdatedRows" FROM "EquityOwnerMigrationProgress" WHERE "TableName" = 'FinancialFact'"""
                )
            )
                .Should()
                .Be(10000);
            await Execute(
                connection,
                """
                INSERT INTO "FinancialFact" ("Id", "CommonStockId", "FinancialConceptId", "Unit", "PeriodType", "PeriodStart", "PeriodEnd", "Value", "FiscalYear", "FiscalPeriod", "Form", "FiledDate", "AccessionNumber", "DimensionsKey", "CreationTime")
                SELECT '00000000-0000-0000-0000-000000030000', "CommonStockId", "FinancialConceptId", "Unit", "PeriodType", "PeriodStart", "PeriodEnd", "Value", "FiscalYear", "FiscalPeriod", "Form", "FiledDate", 'old-writer', "DimensionsKey", "CreationTime"
                FROM "FinancialFact" WHERE "Id" = '00000000-0000-0000-0000-000000000001';
                INSERT INTO "FinancialFact" ("Id", "EquityIssuerId", "FinancialConceptId", "Unit", "PeriodType", "PeriodStart", "PeriodEnd", "Value", "FiscalYear", "FiscalPeriod", "Form", "FiledDate", "AccessionNumber", "DimensionsKey", "CreationTime")
                SELECT '00000000-0000-0000-0000-000000000000', "CommonStockId", "FinancialConceptId", "Unit", "PeriodType", "PeriodStart", "PeriodEnd", "Value", "FiscalYear", "FiscalPeriod", "Form", "FiledDate", 'native-writer', "DimensionsKey", "CreationTime"
                FROM "FinancialFact" WHERE "Id" = '00000000-0000-0000-0000-000000000001';
                INSERT INTO "OriginalFactRows" SELECT to_jsonb(f) - 'EquityIssuerId' FROM "FinancialFact" f
                WHERE "AccessionNumber" IN ('old-writer', 'native-writer');
                DROP TRIGGER "99_stop_second_batch" ON "FinancialFact";
                """
            );
            var interruptedIndex = () =>
                Execute(
                    connection,
                    """
                    CREATE UNIQUE INDEX CONCURRENTLY "IX_FinancialFact_EquityIssuerId_FinancialConceptId_PeriodEnd"
                    ON "FinancialFact" ("CommonStockId");
                    """
                );
            (await interruptedIndex.Should().ThrowAsync<PostgresException>())
                .Which.SqlState.Should()
                .Be(PostgresErrorCodes.UniqueViolation);
            await Apply(connection, "FinancialFact");
            (
                await Scalar<long>(
                    connection,
                    """SELECT "UpdatedRows" FROM "EquityOwnerMigrationProgress" WHERE "TableName" = 'FinancialFact'"""
                )
            )
                .Should()
                .Be(20003);
            (
                await Scalar<bool>(
                    connection,
                    """SELECT "Completed" FROM "EquityOwnerMigrationProgress" WHERE "TableName" = 'FinancialFact'"""
                )
            )
                .Should()
                .BeTrue();
            await VerifyFacts(connection);
            var indexBeforeReplay = await Scalar<uint>(
                connection,
                """SELECT '"IX_FinancialFact_EquityIssuerId_FinancialConceptId_PeriodEnd"'::regclass::oid"""
            );
            await Apply(connection, "FinancialFact");
            (
                await Scalar<uint>(
                    connection,
                    """SELECT '"IX_FinancialFact_EquityIssuerId_FinancialConceptId_PeriodEnd"'::regclass::oid"""
                )
            )
                .Should()
                .Be(indexBeforeReplay);
            await VerifyFacts(connection);

            await Execute(
                connection,
                """
                UPDATE "FinancialFact" SET "EquityIssuerId" = '10000000-0000-0000-0000-000000000002'
                    WHERE "Id" = '00000000-0000-0000-0000-000000000001';
                """
            );
            (
                await Scalar<Guid>(
                    connection,
                    """SELECT "CommonStockId" FROM "FinancialFact" WHERE "Id" = '00000000-0000-0000-0000-000000000001'"""
                )
            )
                .Should()
                .Be(Guid.Parse("10000000-0000-0000-0000-000000000002"));
            await Execute(
                connection,
                """
                UPDATE "FinancialFact" SET "CommonStockId" = '10000000-0000-0000-0000-000000000001'
                    WHERE "Id" = '00000000-0000-0000-0000-000000000001';
                """
            );
            await VerifyFacts(connection);
            var conflicting = () =>
                Execute(
                    connection,
                    """
                    UPDATE "FinancialFact" SET "CommonStockId" = '10000000-0000-0000-0000-000000000002',
                        "EquityIssuerId" = '10000000-0000-0000-0000-000000000003'
                        WHERE "Id" = '00000000-0000-0000-0000-000000000001';
                    """
                );
            await conflicting.Should().ThrowAsync<PostgresException>();
            await VerifyFacts(connection);
        });
    }

    [Fact]
    public async Task CompositeKeyBackfillPreservesEveryQuarterAndAcceptsBothWriterGenerations()
    {
        await WithIsolatedSchema(async connection =>
        {
            await Execute(
                connection,
                """
                CREATE TABLE "EquityIssuer" ("Id" uuid PRIMARY KEY);
                INSERT INTO "EquityIssuer" VALUES ('10000000-0000-0000-0000-000000000001');
                CREATE TABLE "StockQuarterlyListingActivity" (LIKE public."StockQuarterlyListingActivity" INCLUDING ALL);
                ALTER TABLE "StockQuarterlyListingActivity" DROP COLUMN "EquityIssuerId" CASCADE;
                INSERT INTO "StockQuarterlyListingActivity" ("CommonStockId", "ReportDate", "IsCombined", "PriceSeriesTicker", "CurrentShares", "PreviousShares", "ComputedAt")
                SELECT '10000000-0000-0000-0000-000000000001', '2025-12-31', false,
                    lpad(i::text, 8, '0'), 123456789 + i, -123456789 - i, '2026-03-16T01:02:03.123456Z' FROM generate_series(1, 10003) i;
                CREATE TABLE "OriginalQuarterRows" AS SELECT to_jsonb(q) AS payload FROM "StockQuarterlyListingActivity" q;
                """
            );
            await Apply(connection, "StockQuarterlyListingActivity");
            (
                await Scalar<long>(
                    connection,
                    """
                    SELECT count(*) FROM (
                        (SELECT payload FROM "OriginalQuarterRows" EXCEPT ALL SELECT to_jsonb(q) - 'EquityIssuerId' FROM "StockQuarterlyListingActivity" q)
                        UNION ALL
                        (SELECT to_jsonb(q) - 'EquityIssuerId' FROM "StockQuarterlyListingActivity" q EXCEPT ALL SELECT payload FROM "OriginalQuarterRows")
                    ) differences
                    """
                )
            ).Should().Be(0);
            await Execute(
                connection,
                """
                INSERT INTO "StockQuarterlyListingActivity" ("CommonStockId", "ReportDate", "IsCombined", "PriceSeriesTicker", "CurrentShares", "PreviousShares", "ComputedAt")
                VALUES ('10000000-0000-0000-0000-000000000001', '2026-03-31', false, 'OLD', 99, 88, now());
                INSERT INTO "StockQuarterlyListingActivity" ("EquityIssuerId", "ReportDate", "IsCombined", "PriceSeriesTicker", "CurrentShares", "PreviousShares", "ComputedAt")
                VALUES ('10000000-0000-0000-0000-000000000001', '2026-03-31', false, 'NEW', 77, 66, now())
                ON CONFLICT ("EquityIssuerId", "ReportDate", "IsCombined", "PriceSeriesTicker") DO UPDATE SET "CurrentShares" = EXCLUDED."CurrentShares";
                """
            );
            (
                await Scalar<long>(
                    connection,
                    """SELECT count(*) FROM "StockQuarterlyListingActivity" WHERE "EquityIssuerId" IS DISTINCT FROM "CommonStockId";"""
                )
            )
                .Should()
                .Be(0);
            (
                await Scalar<long>(
                    connection,
                    """SELECT count(*) FROM "StockQuarterlyListingActivity";"""
                )
            )
                .Should()
                .Be(10005);
        });
    }

    [Fact]
    public async Task CanonicalOwnerChangesInvalidateAppliedSplitsAndSynchronizeHistoricalSeries()
    {
        await using var context = fixture.CreateDbContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var first = new CommonStock { Ticker = "OWNER-FIRST" };
        var second = new CommonStock { Ticker = "OWNER-SECOND" };
        context.AddRange(first, second);
        await context.SaveChangesAsync();
        var split = new StockSplit
        {
            EquityIssuerId = first.Id,
            PriceSeriesTicker = "HISTORICAL-SERIES",
            EffectiveDate = new DateOnly(2025, 1, 2),
            Numerator = 2,
            Denominator = 1,
            PriceAdjustmentAppliedTime = new DateTime(2025, 1, 3, 0, 0, 0, DateTimeKind.Utc),
        };
        context.Add(split);
        await context.SaveChangesAsync();
        (
            await context
                .Set<LegacyEquityListing>()
                .AnyAsync(row =>
                    row.CommonStockId == second.Id && row.ListedTicker == split.PriceSeriesTicker
                )
        )
            .Should()
            .BeFalse();
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE "StockSplit" SET "EquityIssuerId" = {second.Id} WHERE "Id" = {split.Id};
            """
        );
        await context.Entry(split).ReloadAsync();
        split.PriceAdjustmentAppliedTime.Should().BeNull();
        (
            await context
                .Set<LegacyEquityListing>()
                .AnyAsync(row =>
                    row.CommonStockId == second.Id && row.ListedTicker == split.PriceSeriesTicker
                )
        )
            .Should()
            .BeTrue();
        (
            await context
                .Database.SqlQueryRaw<long>(
                    """
                    SELECT count(*) AS "Value" FROM pg_trigger trigger
                    JOIN pg_attribute previous ON previous.attrelid = trigger.tgrelid AND previous.attname = 'CommonStockId'
                    JOIN pg_attribute canonical ON canonical.attrelid = trigger.tgrelid AND canonical.attname = 'EquityIssuerId'
                    WHERE trigger.tgname IN ('equity_identity_series_write', 'TR_StockSplit_InvalidateRevisedAdjustment')
                        AND previous.attnum = ANY(trigger.tgattr) AND NOT canonical.attnum = ANY(trigger.tgattr)
                    """
                )
                .SingleAsync()
        ).Should().Be(0);
    }

    [Fact]
    public void ValidationRunsAfterConstraintAdditionTransactionHasCommitted()
    {
        var operations = new ExpandCanonicalEquityOwnerColumns()
            .UpOperations.Cast<SqlOperation>()
            .ToList();
        var validations = operations
            .Where(operation =>
                operation.Sql.Contains("VALIDATE CONSTRAINT", StringComparison.Ordinal)
            )
            .ToList();
        validations.Should().NotBeEmpty();
        validations
            .Should()
            .OnlyContain(operation =>
                operation.SuppressTransaction
                && !operation.Sql.Contains("ADD CONSTRAINT", StringComparison.Ordinal)
            );
        operations
            .Where(operation => operation.Sql.StartsWith("DO $expand$", StringComparison.Ordinal))
            .Should()
            .OnlyContain(operation => operation.SuppressTransaction);
    }

    private static async Task VerifyFacts(NpgsqlConnection connection)
    {
        (
            await Scalar<long>(
                connection,
                """
                SELECT count(*) FROM (
                    (SELECT payload FROM "OriginalFactRows" EXCEPT ALL SELECT to_jsonb(f) - 'EquityIssuerId' FROM "FinancialFact" f)
                    UNION ALL
                    (SELECT to_jsonb(f) - 'EquityIssuerId' FROM "FinancialFact" f EXCEPT ALL SELECT payload FROM "OriginalFactRows")
                ) differences
                """
            )
        ).Should().Be(0);
        (
            await Scalar<long>(
                connection,
                """SELECT count(*) FROM "FinancialFact" WHERE "EquityIssuerId" IS DISTINCT FROM "CommonStockId";"""
            )
        )
            .Should()
            .Be(0);
    }

    private static async Task Apply(NpgsqlConnection connection, string table)
    {
        var marker = '"' + table + '"';
        var operations = new ExpandCanonicalEquityOwnerColumns()
            .UpOperations.Cast<SqlOperation>()
            .ToList();
        var indexes = operations
            .Where(op =>
                op.Sql.Contains(marker, StringComparison.Ordinal)
                && (
                    op.Sql.StartsWith("CREATE INDEX", StringComparison.Ordinal)
                    || op.Sql.StartsWith("CREATE UNIQUE INDEX", StringComparison.Ordinal)
                )
            )
            .Select(op => op.Sql.Split('"')[1])
            .ToHashSet();
        foreach (var operation in operations)
        {
            if (
                operation.Sql.Contains(marker, StringComparison.Ordinal)
                || operation.Sql.StartsWith("DROP INDEX", StringComparison.Ordinal)
                    && indexes.Contains(operation.Sql.Split('"')[1])
                || operation.Sql.StartsWith(
                    "CREATE TABLE IF NOT EXISTS \"EquityOwnerMigrationProgress\"",
                    StringComparison.Ordinal
                )
                || operation.Sql.StartsWith(
                    "CREATE OR REPLACE FUNCTION \"eq_mirror_canonical_owner_",
                    StringComparison.Ordinal
                )
            )
                await Execute(connection, operation.Sql);
        }
    }

    private async Task WithIsolatedSchema(Func<NpgsqlConnection, Task> test)
    {
        var schema = "equity_owner_" + Guid.NewGuid().ToString("N");
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        try
        {
            await Execute(connection, $"CREATE SCHEMA {schema}; SET search_path TO {schema};");
            await test(connection);
        }
        finally
        {
            await Execute(connection, $"SET search_path TO public; DROP SCHEMA {schema} CASCADE;");
        }
    }

    private static async Task Execute(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection) { CommandTimeout = 120 };
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<T> Scalar<T>(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection) { CommandTimeout = 120 };
        return (T)(await command.ExecuteScalarAsync())!;
    }
}

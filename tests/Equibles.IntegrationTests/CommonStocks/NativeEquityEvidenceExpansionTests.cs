using Equibles.CommonStocks.Data.Models;
using Equibles.IntegrationTests.Helpers;
using Equibles.Migrations.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Npgsql;

namespace Equibles.IntegrationTests.CommonStocks;

[Collection(ParadeDbCollection.Name)]
public class NativeEquityEvidenceExpansionTests(ParadeDbFixture fixture)
{
    private static readonly (string Previous, string Canonical)[] Tables =
    [
        ("CommonStockCusipAlias", "EquityIssuerCusipAlias"),
        ("CommonStockTickerAlias", "EquityIssuerTickerAlias"),
        ("CommonStockTickerEvidence", "EquityIssuerTickerEvidence"),
        ("CommonStockListedCusip", "EquityListingCusipEvidence"),
        ("CommonStockDelistedListing", "EquityListingRetirementEvidence"),
        ("ListedSecurity", "IssuerSecurityRegistration"),
    ];

    [Fact]
    public async Task PopulatedExpansionPreservesAllSixOriginalRowsAndReplaysWithoutChangingIds()
    {
        await WithIsolatedSchema(async connection =>
        {
            await Execute(
                connection,
                """
                CREATE TABLE "EquityIssuer" ("Id" uuid PRIMARY KEY);
                INSERT INTO "EquityIssuer" VALUES ('10000000-0000-0000-0000-000000000001');
                """
            );
            foreach (var (previous, _) in Tables)
            {
                await Execute(
                    connection,
                    $"""
                    CREATE TABLE "{previous}" (LIKE public."{previous}" INCLUDING ALL);
                    CREATE TRIGGER "00_equity_owner_columns" BEFORE INSERT OR UPDATE ON "{previous}"
                        FOR EACH ROW EXECUTE FUNCTION public.eq_mirror_canonical_owner_commonstockid();
                    """
                );
            }
            await Execute(
                connection,
                """
                INSERT INTO "CommonStockCusipAlias" ("Id", "EquityIssuerId", "Cusip", "CreationTime")
                    VALUES ('20000000-0000-0000-0000-000000000001', '10000000-0000-0000-0000-000000000001', '012345678', '2026-01-02T03:04:05.123456Z');
                INSERT INTO "CommonStockTickerAlias" ("Id", "EquityIssuerId", "Ticker", "CreationTime")
                    VALUES ('20000000-0000-0000-0000-000000000002', '10000000-0000-0000-0000-000000000001', 'OLD-A', '2026-01-02T03:04:05.123456Z');
                INSERT INTO "CommonStockTickerEvidence" ("Id", "EquityIssuerId", "Ticker", "FiledDate", "SourceDocumentId", "AccessionNumber")
                    SELECT ('00000000-0000-0000-0000-' || lpad(i::text, 12, '0'))::uuid,
                        '10000000-0000-0000-0000-000000000001', 'FORMER-' || i, '2025-12-31',
                        '30000000-0000-0000-0000-000000000001', 'fonte-ação-' || i FROM generate_series(1, 10003) i;
                INSERT INTO "CommonStockListedCusip" ("Id", "EquityIssuerId", "ListedTicker", "Cusip", "CreationTime")
                    VALUES ('20000000-0000-0000-0000-000000000004', '10000000-0000-0000-0000-000000000001', 'CLASS-B', '987654321', '2026-01-02T03:04:05.123456Z');
                INSERT INTO "CommonStockDelistedListing" ("Id", "EquityIssuerId", "ListedTicker", "DelistedOn", "HistoricalPriceBackfillAttemptedAt", "Cusip",
                    "HistoricalCusipBackfillRequestedAt", "HistoricalCusipBackfillCandidates", "HistoricalCusipBackfillCandidateOn", "HistoricalCusipBackfillAmbiguous", "HistoricalCusipBackfillSweepStartedAt")
                    VALUES ('20000000-0000-0000-0000-000000000005', '10000000-0000-0000-0000-000000000001', 'RETIRED', '2025-11-30', '2026-01-02T03:04:05.123456Z', NULL,
                        '2026-02-03T04:05:06.987654Z', ARRAY['012345678', '987654321'], '2025-11-29', true, NULL);
                INSERT INTO "ListedSecurity" ("Id", "EquityIssuerId", "TradingSymbol", "Title", "ExchangeName", "AccessionNumber", "FiledDate")
                    VALUES ('20000000-0000-0000-0000-000000000006', '10000000-0000-0000-0000-000000000001', 'CLASS-B', 'Ações ordinárias — classe B', NULL, 'original-filing', '2025-12-31');
                """
            );
            foreach (var (previous, _) in Tables)
                await Execute(
                    connection,
                    $"""CREATE TABLE "Original_{previous}" AS SELECT to_jsonb(row) AS payload FROM "{previous}" row;"""
                );
            await Apply(connection);
            await VerifyOriginals(connection);
            await Apply(connection);
            await VerifyOriginals(connection);
        });
    }

    [Fact]
    public async Task BothWriterGenerationsPreserveUpsertsDeletesAndAtomicConflictRefusals()
    {
        await using var context = fixture.CreateDbContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var issuer = new EquityIssuer { Name = "Native evidence owner" };
        context.Add(issuer);
        await context.SaveChangesAsync();
        var alias = new EquityIssuerTickerAlias
        {
            EquityIssuerId = issuer.Id,
            Ticker = "ALIAS-ORIGINAL",
        };
        context.Add(alias);
        await context.SaveChangesAsync();
        (
            await context
                .Database.SqlQuery<Guid>(
                    $"""SELECT "CommonStockId" AS "Value" FROM "CommonStockTickerAlias" WHERE "Id" = {alias.Id}"""
                )
                .SingleAsync()
        )
            .Should()
            .Be(issuer.Id);
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO "CommonStockTickerAlias" ("Id", "CommonStockId", "Ticker", "CreationTime")
                VALUES ({Guid.NewGuid()}, {issuer.Id}, 'ALIAS-ORIGINAL', '2026-01-02T03:04:05.123456Z')
                ON CONFLICT ("Ticker") DO UPDATE SET "CreationTime" = EXCLUDED."CreationTime";
            """
        );
        await context.Entry(alias).ReloadAsync();
        alias
            .CreationTime.Should()
            .Be(new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc).AddTicks(1234560));
        alias.Ticker = "ALIAS-RENAMED";
        await context.SaveChangesAsync();
        (
            await context
                .Database.SqlQuery<string>(
                    $"""SELECT "Ticker" AS "Value" FROM "CommonStockTickerAlias" WHERE "Id" = {alias.Id}"""
                )
                .SingleAsync()
        )
            .Should()
            .Be("ALIAS-RENAMED");
        await transaction.CreateSavepointAsync("conflict");
        var conflicting = () =>
            context.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO "CommonStockTickerAlias" ("Id", "CommonStockId", "Ticker", "CreationTime")
                    VALUES ({Guid.NewGuid()}, {issuer.Id}, 'ALIAS-RENAMED', now());
                """
            );
        (await conflicting.Should().ThrowAsync<PostgresException>())
            .Which.SqlState.Should()
            .Be(PostgresErrorCodes.UniqueViolation);
        await transaction.RollbackToSavepointAsync("conflict");
        (
            await context
                .Set<EquityIssuerTickerAlias>()
                .CountAsync(row => row.EquityIssuerId == issuer.Id)
        )
            .Should()
            .Be(1);
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"""DELETE FROM "CommonStockTickerAlias" WHERE "Id" = {alias.Id};"""
        );
        (await context.Set<EquityIssuerTickerAlias>().AnyAsync(row => row.Id == alias.Id))
            .Should()
            .BeFalse();
        context.ChangeTracker.Clear();
        var other = new EquityIssuerTickerAlias
        {
            EquityIssuerId = issuer.Id,
            Ticker = "DELETE-NATIVE",
        };
        context.Add(other);
        await context.SaveChangesAsync();
        context.Remove(other);
        await context.SaveChangesAsync();
        (
            await context
                .Database.SqlQuery<long>(
                    $"""SELECT count(*) AS "Value" FROM "CommonStockTickerAlias" WHERE "Id" = {other.Id}"""
                )
                .SingleAsync()
        )
            .Should()
            .Be(0);
    }

    [Fact]
    public async Task AnUnexpectedOriginalFieldStopsTheCopyWithoutLosingItsContents()
    {
        await WithIsolatedSchema(async connection =>
        {
            await Execute(
                connection,
                """
                CREATE TABLE "CommonStockTickerEvidence" (LIKE public."CommonStockTickerEvidence" INCLUDING ALL);
                ALTER TABLE "CommonStockTickerEvidence" ADD COLUMN "OriginalEvidence" text;
                INSERT INTO "CommonStockTickerEvidence" ("Id", "CommonStockId", "EquityIssuerId", "Ticker", "FiledDate", "SourceDocumentId", "AccessionNumber", "OriginalEvidence")
                    VALUES ('20000000-0000-0000-0000-000000000001', '10000000-0000-0000-0000-000000000001',
                        '10000000-0000-0000-0000-000000000001', 'ORIGINAL', '2025-12-31',
                        '30000000-0000-0000-0000-000000000001', 'original-filing', 'Preserve this original source field.');
                """
            );
            var migrate = () => Apply(connection, "CommonStockTickerEvidence");
            await migrate
                .Should()
                .ThrowAsync<PostgresException>()
                .WithMessage("*Unexpected original columns*");
            await using var command = new NpgsqlCommand(
                """
                SELECT "OriginalEvidence" FROM "CommonStockTickerEvidence"
                    WHERE to_regclass('"EquityIssuerTickerEvidence"') IS NULL;
                """,
                connection
            );
            (await command.ExecuteScalarAsync())
                .Should()
                .Be("Preserve this original source field.");
        });
    }

    [Fact]
    public async Task CopyTransactionAllowsAnUnrelatedIssuerUpdate()
    {
        await WithIsolatedSchema(async connection =>
        {
            await Execute(
                connection,
                """
                CREATE TABLE "EquityIssuer" ("Id" uuid PRIMARY KEY, "Name" text);
                INSERT INTO "EquityIssuer" VALUES
                    ('10000000-0000-0000-0000-000000000001', 'Evidence owner'),
                    ('10000000-0000-0000-0000-000000000002', 'Unrelated issuer');
                CREATE TABLE "CommonStockCusipAlias" (LIKE public."CommonStockCusipAlias" INCLUDING ALL);
                INSERT INTO "CommonStockCusipAlias" ("Id", "CommonStockId", "EquityIssuerId", "Cusip", "CreationTime")
                    VALUES ('20000000-0000-0000-0000-000000000001', '10000000-0000-0000-0000-000000000001',
                        '10000000-0000-0000-0000-000000000001', '012345678', '2026-01-02T03:04:05.123456Z');
                """
            );
            var operations = new ExpandNativeEquityEvidenceTables()
                .UpOperations.Cast<SqlOperation>()
                .Where(operation =>
                    operation.Sql.Contains("\"CommonStockCusipAlias\"", StringComparison.Ordinal)
                )
                .ToList();
            var copy = operations.Single(operation =>
                operation.Sql.Contains(
                    "INSERT INTO \"EquityIssuerCusipAlias\" (",
                    StringComparison.Ordinal
                )
            );
            foreach (var setup in operations.TakeWhile(operation => operation != copy))
            {
                await using var setupTransaction = await connection.BeginTransactionAsync();
                await Execute(connection, setup.Sql);
                await setupTransaction.CommitAsync();
            }
            await using var copyTransaction = await connection.BeginTransactionAsync();
            await Execute(connection, copy.Sql);
            await using var schemaCommand = new NpgsqlCommand(
                "SELECT current_schema()",
                connection
            );
            var schema = (string)(await schemaCommand.ExecuteScalarAsync())!;
            await using var concurrent = new NpgsqlConnection(fixture.ConnectionString);
            await concurrent.OpenAsync();
            await Execute(concurrent, $"SET search_path TO {schema}; SET lock_timeout = '1s';");
            await using var update = new NpgsqlCommand(
                """
                UPDATE "EquityIssuer" SET "Name" = 'Updated while evidence copy is uncommitted'
                    WHERE "Id" = '10000000-0000-0000-0000-000000000002';
                """,
                concurrent
            )
            {
                CommandTimeout = 5,
            };
            (await update.ExecuteNonQueryAsync()).Should().Be(1);
            await copyTransaction.CommitAsync();
        });
    }

    private static async Task Apply(NpgsqlConnection connection, string table = null)
    {
        foreach (
            var operation in new ExpandNativeEquityEvidenceTables().UpOperations.Cast<SqlOperation>()
        )
        {
            if (
                table != null
                && !operation.Sql.Contains('"' + table + '"', StringComparison.Ordinal)
            )
                continue;
            if (operation.SuppressTransaction)
            {
                await Execute(connection, operation.Sql);
                continue;
            }
            await using var transaction = await connection.BeginTransactionAsync();
            await Execute(connection, operation.Sql);
            await transaction.CommitAsync();
        }
    }

    private static async Task VerifyOriginals(NpgsqlConnection connection)
    {
        foreach (var (previous, canonical) in Tables)
        {
            await using var command = new NpgsqlCommand(
                $"""
                SELECT count(*) FROM (
                    (SELECT payload FROM "Original_{previous}" EXCEPT ALL SELECT to_jsonb(row) FROM "{previous}" row)
                    UNION ALL
                    (SELECT to_jsonb(row) FROM "{previous}" row EXCEPT ALL SELECT payload FROM "Original_{previous}")
                    UNION ALL
                    (SELECT payload - 'CommonStockId' FROM "Original_{previous}" EXCEPT ALL SELECT to_jsonb(row) FROM "{canonical}" row)
                    UNION ALL
                    (SELECT to_jsonb(row) FROM "{canonical}" row EXCEPT ALL SELECT payload - 'CommonStockId' FROM "Original_{previous}")
                ) differences
                """,
                connection
            );
            ((long)(await command.ExecuteScalarAsync())!).Should().Be(0, previous);
        }
    }

    private async Task WithIsolatedSchema(Func<NpgsqlConnection, Task> test)
    {
        var schema = "equity_evidence_" + Guid.NewGuid().ToString("N");
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
}

using Equibles.IntegrationTests.Helpers;
using Equibles.Migrations.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Npgsql;

namespace Equibles.IntegrationTests.CommonStocks;

[Collection(ParadeDbCollection.Name)]
public class NativeIssuerForeignKeyRetargetTests(ParadeDbFixture fixture)
{
    [Fact]
    public async Task InterruptedRetargetPreservesFactsAndValidationAllowsConcurrentWrites()
    {
        var schema = "issuer_fk_" + Guid.NewGuid().ToString("N");
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        try
        {
            await Execute(connection, $"CREATE SCHEMA {schema}; SET search_path TO {schema};");
            await Execute(
                connection,
                """
                CREATE TABLE "CommonStock" ("Id" uuid PRIMARY KEY);
                CREATE TABLE "EquityIssuer" ("Id" uuid PRIMARY KEY, "Name" text);
                INSERT INTO "CommonStock" VALUES ('10000000-0000-0000-0000-000000000001');
                INSERT INTO "EquityIssuer" VALUES
                    ('10000000-0000-0000-0000-000000000001', 'Original owner'),
                    ('10000000-0000-0000-0000-000000000002', 'Concurrent issuer');
                CREATE TABLE "FinancialFact" ("Id" uuid PRIMARY KEY, "CommonStockId" uuid NOT NULL,
                    "Value" numeric NOT NULL, "OriginalSource" text,
                    CONSTRAINT "FK_FinancialFact_CommonStock_CommonStockId"
                        FOREIGN KEY ("CommonStockId") REFERENCES "CommonStock"("Id") ON DELETE CASCADE);
                INSERT INTO "FinancialFact" SELECT ('00000000-0000-0000-0000-' || lpad(i::text, 12, '0'))::uuid,
                    '10000000-0000-0000-0000-000000000001', -123456789012345.123456789 - i, 'Fonte original — ação ' || i
                    FROM generate_series(1, 30003) i;
                CREATE TABLE "OriginalRows" AS SELECT to_jsonb(f) AS payload FROM "FinancialFact" f;
                """
            );
            var operations = new RetargetFinancialFactsToIssuers()
                .UpOperations.Cast<SqlOperation>()
                .Where(operation =>
                    operation.Sql.Contains("\"FinancialFact\"", StringComparison.Ordinal)
                )
                .ToArray();
            operations.Should().HaveCount(2);
            var setup = operations[0];
            var validate = operations[1];
            setup.SuppressTransaction.Should().BeFalse();
            validate.SuppressTransaction.Should().BeTrue();
            await using (var transaction = await connection.BeginTransactionAsync())
            {
                await Execute(connection, setup.Sql);
                await transaction.CommitAsync();
            }
            (
                await Scalar<bool>(
                    connection,
                    """
                    SELECT convalidated FROM pg_constraint
                        WHERE conrelid = '"FinancialFact"'::regclass AND conname = 'FK_FinancialFact_EquityIssuer_CommonStockId';
                    """
                )
            ).Should().BeFalse();
            // A restart after metadata commit still preserves history when a retiring directory row disappears.
            await Execute(connection, """DELETE FROM "CommonStock";""");
            (await Scalar<long>(connection, """SELECT count(*) FROM "FinancialFact";"""))
                .Should()
                .Be(30003);
            await using (var retry = await connection.BeginTransactionAsync())
            {
                await Execute(connection, setup.Sql);
                await retry.CommitAsync();
            }
            await using (var validating = await connection.BeginTransactionAsync())
            {
                await Execute(connection, validate.Sql);
                // Keep validation's actual locks held while another connection writes both sides.
                await using var concurrent = new NpgsqlConnection(fixture.ConnectionString);
                await concurrent.OpenAsync();
                await Execute(concurrent, $"SET search_path TO {schema}; SET lock_timeout = '1s';");
                await Execute(
                    concurrent,
                    """
                    UPDATE "EquityIssuer" SET "Name" = 'Updated during validation'
                        WHERE "Id" = '10000000-0000-0000-0000-000000000002';
                    INSERT INTO "FinancialFact" VALUES ('90000000-0000-0000-0000-000000000001',
                        '10000000-0000-0000-0000-000000000001', 123.456789, 'Concurrent original writer');
                    """
                );
                await validating.CommitAsync();
            }
            (
                await Scalar<long>(
                    connection,
                    """
                    SELECT count(*) FROM (
                        (SELECT payload FROM "OriginalRows" EXCEPT ALL SELECT to_jsonb(f) FROM "FinancialFact" f
                            WHERE "Id" <> '90000000-0000-0000-0000-000000000001')
                        UNION ALL
                        (SELECT to_jsonb(f) FROM "FinancialFact" f WHERE "Id" <> '90000000-0000-0000-0000-000000000001'
                            EXCEPT ALL SELECT payload FROM "OriginalRows")
                    ) differences;
                    """
                )
            ).Should().Be(0);
            (await Scalar<long>(connection, """SELECT count(*) FROM "FinancialFact";"""))
                .Should()
                .Be(30004);
            var invalid = () =>
                Execute(
                    connection,
                    """
                    INSERT INTO "FinancialFact" VALUES ('90000000-0000-0000-0000-000000000002',
                        '10000000-0000-0000-0000-000000000099', 1, 'Unresolved owner');
                    """
                );
            (await invalid.Should().ThrowAsync<PostgresException>())
                .Which.SqlState.Should()
                .Be(PostgresErrorCodes.ForeignKeyViolation);
        }
        finally
        {
            await Execute(connection, $"SET search_path TO public; DROP SCHEMA {schema} CASCADE;");
        }
    }

    private static async Task Execute(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection) { CommandTimeout = 60 };
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<T> Scalar<T>(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection) { CommandTimeout = 60 };
        return (T)(await command.ExecuteScalarAsync())!;
    }
}

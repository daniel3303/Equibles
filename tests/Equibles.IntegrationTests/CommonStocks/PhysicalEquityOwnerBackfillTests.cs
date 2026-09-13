using Equibles.IntegrationTests.Helpers;
using Equibles.Migrations.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Npgsql;

namespace Equibles.IntegrationTests.CommonStocks;

[Collection(ParadeDbCollection.Name)]
public class PhysicalEquityOwnerBackfillTests(ParadeDbFixture fixture)
{
    [Fact]
    public async Task EfReplaysTheEarlierMissingMigrationWithoutChangingAnExpandedSchema()
    {
        const string preparation = "20260912160400_PrepareCanonicalOwnerBackfill";
        await using var context = fixture.CreateNativeDbContext();
        var migrations = context.Database.GetMigrations().ToArray();
        Array
            .IndexOf(migrations, preparation)
            .Should()
            .BeGreaterThanOrEqualTo(0)
            .And.BeLessThan(
                Array.IndexOf(migrations, "20260912160424_ExpandCanonicalEquityOwnerColumns")
            );
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        const string inventory = """
            SELECT jsonb_build_object(
                'relations',(SELECT jsonb_agg(jsonb_build_array(oid,relname,relkind) ORDER BY oid)
                    FROM pg_class WHERE relnamespace='public'::regnamespace),
                'functions',(SELECT jsonb_agg(jsonb_build_array(oid,proname,prosrc) ORDER BY oid)
                    FROM pg_proc WHERE pronamespace='public'::regnamespace),
                'constraints',(SELECT jsonb_agg(jsonb_build_array(oid,conname,pg_get_constraintdef(oid)) ORDER BY oid)
                    FROM pg_constraint WHERE connamespace='public'::regnamespace))::text;
            """;
        var before = await Scalar<string>(connection, inventory);
        await Execute(
            connection,
            $"DELETE FROM \"__EFMigrationsHistory\" WHERE \"MigrationId\"='{preparation}';"
        );
        try
        {
            (await context.Database.GetPendingMigrationsAsync()).Should().Equal(preparation);
            await context.Database.MigrateAsync();
            (await Scalar<string>(connection, inventory)).Should().Be(before);
            (await context.Database.GetPendingMigrationsAsync()).Should().BeEmpty();
            context.Database.HasPendingModelChanges().Should().BeFalse();
        }
        finally
        {
            await context.Database.MigrateAsync();
        }
    }

    [Fact]
    public async Task ParameterizedBatchUsesPhysicalRangesWithTheNormalPlanner()
    {
        await WithSchema(async connection =>
        {
            await Seed(connection, 50000);
            await Execute(connection, Operations()[0].Sql);
            await Execute(
                connection,
                """
                SET plan_cache_mode=force_generic_plan;
                PREPARE owner_range(bigint,bigint) AS UPDATE "FinancialFact" SET "EquityIssuerId"="CommonStockId"
                    WHERE ctid >= format('(%s,0)',$1)::tid AND ctid < format('(%s,0)',$2)::tid
                    AND "EquityIssuerId" IS NULL AND "CommonStockId" IS NOT NULL;
                """
            );
            var plan = await Scalar<string>(
                connection,
                "EXPLAIN (FORMAT JSON) EXECUTE owner_range(0,1024);"
            );
            plan.Should().Contain("Tid Range Scan").And.NotContain("Seq Scan");
        });
    }

    [Fact]
    public async Task AValidatedConstraintWithTheWrongExpressionCannotCertifyCompletion()
    {
        await WithSchema(async connection =>
        {
            await Seed(connection, 2);
            await Execute(
                connection,
                """
                ALTER TABLE "FinancialFact" ADD COLUMN "EquityIssuerId" uuid;
                ALTER TABLE "FinancialFact" ADD CONSTRAINT "CK_FinancialFact_CanonicalOwnerMirror" CHECK (true);
                """
            );
            var apply = () => Apply(connection);
            await apply
                .Should()
                .ThrowAsync<PostgresException>()
                .WithMessage("*Unexpected canonical owner proof*");
            (
                await Scalar<long>(
                    connection,
                    """SELECT count(*) FROM "EquityOwnerMigrationProgress" WHERE "Completed";"""
                )
            )
                .Should()
                .Be(0);
        });
    }

    [Fact]
    public async Task InterruptedPhysicalTraversalPreservesRowsAndResumesWithBothWriterGenerations()
    {
        await WithSchema(async connection =>
        {
            await Seed(connection, 50000);
            await Execute(
                connection,
                """
                CREATE TABLE "OriginalRows" AS SELECT to_jsonb(f) AS payload FROM "FinancialFact" f;
                CREATE FUNCTION interrupt_range() RETURNS trigger LANGUAGE plpgsql AS $f$
                BEGIN IF NEW."Ordinal" = 45000 THEN RAISE EXCEPTION 'interrupted physical batch'; END IF; RETURN NEW; END $f$;
                CREATE TRIGGER "99_interrupt_range" BEFORE UPDATE ON "FinancialFact"
                    FOR EACH ROW EXECUTE FUNCTION interrupt_range();
                """
            );
            var interrupted = () => Apply(connection);
            await interrupted
                .Should()
                .ThrowAsync<PostgresException>()
                .WithMessage("*interrupted physical batch*");
            var updated = await Scalar<long>(
                connection,
                """SELECT count(*) FROM "FinancialFact" WHERE "EquityIssuerId" IS NOT NULL"""
            );
            updated.Should().BeGreaterThan(0).And.BeLessThan(50000);
            (
                await Scalar<long>(
                    connection,
                    """SELECT "NextBlock" FROM "EquityOwnerPhysicalBackfill" WHERE "TableName"='FinancialFact'"""
                )
            )
                .Should()
                .BeGreaterThan(0);
            await VerifyOriginalRows(connection);
            await Execute(
                connection,
                """
                DROP TRIGGER "99_interrupt_range" ON "FinancialFact";
                UPDATE "FinancialFact" SET "CommonStockId"='10000000-0000-0000-0000-000000000002' WHERE "Ordinal"=1;
                UPDATE "FinancialFact" SET "EquityIssuerId"='10000000-0000-0000-0000-000000000002' WHERE "Ordinal"=45000;
                INSERT INTO "FinancialFact" ("Id","CommonStockId","Ordinal","Value","Payload","CreationTime")
                    SELECT gen_random_uuid(),"CommonStockId",50001,"Value","Payload","CreationTime" FROM "FinancialFact" WHERE "Ordinal"=1;
                INSERT INTO "FinancialFact" ("Id","EquityIssuerId","Ordinal","Value","Payload","CreationTime")
                    SELECT gen_random_uuid(),"EquityIssuerId",50002,"Value","Payload","CreationTime" FROM "FinancialFact" WHERE "Ordinal"=1;
                TRUNCATE "OriginalRows";
                INSERT INTO "OriginalRows" SELECT to_jsonb(f)-'EquityIssuerId' FROM "FinancialFact" f;
                """
            );
            await Apply(connection);
            await VerifyOriginalRows(connection);
            (
                await Scalar<long>(
                    connection,
                    """SELECT count(*) FROM "FinancialFact" WHERE "EquityIssuerId" IS DISTINCT FROM "CommonStockId";"""
                )
            )
                .Should()
                .Be(0);
            (
                await Scalar<long>(
                    connection,
                    """SELECT count(*) FROM "EquityOwnerMigrationProgress" WHERE "Completed";"""
                )
            )
                .Should()
                .Be(2);
            (
                await Scalar<bool>(
                    connection,
                    """SELECT to_regclass('"EquityOwnerPhysicalBackfill"') IS NULL"""
                )
            )
                .Should()
                .BeTrue();
            await Apply(connection);
            await VerifyOriginalRows(connection);
        });
    }

    [Fact]
    public async Task ConflictingNativeOwnerIsRetainedAndRefusesCompletion()
    {
        await WithSchema(async connection =>
        {
            await Seed(connection, 2);
            await Execute(
                connection,
                """
                ALTER TABLE "FinancialFact" ADD COLUMN "EquityIssuerId" uuid;
                UPDATE "FinancialFact" SET "EquityIssuerId"='10000000-0000-0000-0000-000000000002' WHERE "Ordinal"=1;
                """
            );
            var apply = () => Apply(connection);
            await apply.Should().ThrowAsync<PostgresException>();
            (
                await Scalar<Guid>(
                    connection,
                    """SELECT "EquityIssuerId" FROM "FinancialFact" WHERE "Ordinal"=1"""
                )
            )
                .Should()
                .Be(Guid.Parse("10000000-0000-0000-0000-000000000002"));
            (
                await Scalar<long>(
                    connection,
                    """SELECT count(*) FROM "EquityOwnerMigrationProgress" WHERE "Completed";"""
                )
            )
                .Should()
                .Be(0);
        });
    }

    [Fact]
    public async Task ChangedPhysicalRelationRefusesResumeBeforeAdvancingCheckpoint()
    {
        await WithSchema(async connection =>
        {
            await Seed(connection, 2);
            await Execute(connection, Operations()[0].Sql);
            await Execute(connection, "VACUUM FULL \"FinancialFact\";");
            var resume = () => Apply(connection);
            await resume
                .Should()
                .ThrowAsync<PostgresException>()
                .WithMessage("*Relation changed during physical issuer backfill*");
            (
                await Scalar<long>(
                    connection,
                    """SELECT "NextBlock" FROM "EquityOwnerPhysicalBackfill" WHERE "TableName"='FinancialFact'"""
                )
            )
                .Should()
                .Be(0);
            (
                await Scalar<long>(
                    connection,
                    """SELECT count(*) FROM "FinancialFact" WHERE "EquityIssuerId" IS NOT NULL"""
                )
            )
                .Should()
                .Be(0);
        });
    }

    [Theory]
    [InlineData("20260912160424_ExpandCanonicalEquityOwnerColumns")]
    [InlineData("20260912160519_ExpandCanonicalEquityOwnerColumns")]
    public async Task PreviouslyExpandedDatabaseIsUntouched(string migration)
    {
        await WithSchema(async connection =>
        {
            await Execute(
                connection,
                """CREATE TABLE "__EFMigrationsHistory" ("MigrationId" text PRIMARY KEY);"""
            );
            await using var insert = new NpgsqlCommand(
                """INSERT INTO "__EFMigrationsHistory" VALUES (@migration)""",
                connection
            );
            insert.Parameters.AddWithValue("migration", migration);
            await insert.ExecuteNonQueryAsync();
            await Apply(connection);
            (
                await Scalar<long>(
                    connection,
                    "SELECT count(*) FROM pg_tables WHERE schemaname=current_schema()"
                )
            )
                .Should()
                .Be(1);
        });
    }

    [Fact]
    public async Task NativeOnlyRetiredTablesDoNotRecreateCompatibilityStorage()
    {
        await WithSchema(async connection =>
        {
            await Execute(
                connection,
                """
                CREATE TABLE "FinancialFact" ("EquityIssuerId" uuid);
                CREATE TABLE "InstitutionalHolding" ("EquityIssuerId" uuid);
                """
            );
            await Apply(connection);
            (
                await Scalar<long>(
                    connection,
                    "SELECT count(*) FROM pg_tables WHERE schemaname=current_schema()"
                )
            )
                .Should()
                .Be(2);
            (
                await Scalar<long>(
                    connection,
                    "SELECT count(*) FROM pg_proc WHERE pronamespace=current_schema()::regnamespace"
                )
            )
                .Should()
                .Be(0);
        });
    }

    [Fact]
    public async Task ExistingCompletedBackfillWithoutProofFinishesAndRetiresItsNewCheckpoint()
    {
        await WithSchema(async connection =>
        {
            await Seed(connection, 0);
            await Execute(
                connection,
                """
                CREATE TABLE "EquityOwnerMigrationProgress" ("TableName" text PRIMARY KEY, "AfterKey" jsonb,
                    "Completed" boolean NOT NULL DEFAULT false, "UpdatedRows" bigint NOT NULL DEFAULT 0);
                INSERT INTO "EquityOwnerMigrationProgress" ("TableName","Completed","UpdatedRows") VALUES ('FinancialFact',true,123);
                """
            );
            await Apply(connection);
            (
                await Scalar<long>(
                    connection,
                    """SELECT "UpdatedRows" FROM "EquityOwnerMigrationProgress" WHERE "TableName"='FinancialFact'"""
                )
            )
                .Should()
                .Be(123);
            (
                await Scalar<bool>(
                    connection,
                    """SELECT to_regclass('"EquityOwnerPhysicalBackfill"') IS NULL"""
                )
            )
                .Should()
                .BeTrue();
        });
    }

    private static SqlOperation[] Operations() =>
        new PrepareCanonicalOwnerBackfill().UpOperations.Cast<SqlOperation>().ToArray();

    private static async Task Apply(NpgsqlConnection connection)
    {
        foreach (var operation in Operations())
            await Execute(connection, operation.Sql);
    }

    private static Task Seed(NpgsqlConnection connection, int rows) =>
        Execute(
            connection,
            $$"""
            CREATE TABLE "FinancialFact" ("Id" uuid PRIMARY KEY, "CommonStockId" uuid NOT NULL,
                "Ordinal" integer NOT NULL, "Value" numeric(36,12) NOT NULL, "Payload" text NOT NULL, "CreationTime" timestamptz NOT NULL);
            ALTER TABLE "FinancialFact" ALTER COLUMN "Payload" SET STORAGE PLAIN;
            INSERT INTO "FinancialFact" SELECT gen_random_uuid(),'10000000-0000-0000-0000-000000000001',i,
                -123456789012345.123456789123-i,repeat(md5(i::text),16)||'dimensão','2026-03-16T01:02:03.123456Z'
                FROM generate_series(1,{{rows}}) i;
            CREATE TABLE "InstitutionalHolding" (LIKE "FinancialFact" INCLUDING ALL);
            """
        );

    private static async Task VerifyOriginalRows(NpgsqlConnection connection) => (
            await Scalar<long>(
                connection,
                """
                WITH current_rows AS (SELECT to_jsonb(f)-'EquityIssuerId' AS payload FROM "FinancialFact" f)
                SELECT (SELECT count(*) FROM (TABLE current_rows EXCEPT ALL TABLE "OriginalRows") a)
                    +(SELECT count(*) FROM (TABLE "OriginalRows" EXCEPT ALL TABLE current_rows) b);
                """
            )
        ).Should().Be(0);

    private async Task WithSchema(Func<NpgsqlConnection, Task> test)
    {
        var schema = "physical_owner_" + Guid.NewGuid().ToString("N");
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

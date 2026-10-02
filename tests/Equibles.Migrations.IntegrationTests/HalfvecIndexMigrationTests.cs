using Equibles.Migrations.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;

namespace Equibles.Migrations.IntegrationTests;

[Trait("Category", "Postgres")]
public class HalfvecIndexMigrationTests(HalfvecDatabaseFixture fixture)
    : IClassFixture<HalfvecDatabaseFixture>,
        IAsyncLifetime
{
    private const string Index = "IX_Embedding_Qwen3_Halfvec1024_Hnsw";
    private readonly string _databaseName = "halfvec_" + Guid.NewGuid().ToString("N");
    private string _connectionString;
    private HalfvecTestContext _context;
    private static CancellationToken Token => CancellationToken.None;

    public async Task InitializeAsync()
    {
        await ExecuteOn(fixture.Database.GetConnectionString(), $"CREATE DATABASE {_databaseName}");
        _connectionString = new NpgsqlConnectionStringBuilder(
            fixture.Database.GetConnectionString()
        )
        {
            Database = _databaseName,
            Pooling = false,
        }.ConnectionString;
        _context = new HalfvecTestContext(
            new DbContextOptionsBuilder<HalfvecTestContext>()
                .UseNpgsql(
                    _connectionString,
                    options =>
                        options.MigrationsAssembly(typeof(HalfvecTestMigration).Assembly.FullName)
                )
                .Options
        );
        await Execute(
            """
            CREATE EXTENSION IF NOT EXISTS vector;
            CREATE TABLE public."Embedding" (
                "Id" integer PRIMARY KEY, "Vector" vector, "Model" varchar(100), "VectorDimension" integer);
            INSERT INTO public."Embedding"
            SELECT n, ('[1' || repeat(',0',1023) || ']')::vector, 'qwen3-embedding:0.6b', 1024
            FROM generate_series(1,8) n;
            INSERT INTO public."Embedding" VALUES
                (9, '[1,2,3]'::vector, 'other-model', 3),
                (10, '[3,2,1]'::vector, 'qwen3-embedding:0.6b', 3);
            CREATE INDEX "ExistingEmbeddingIdIndex" ON public."Embedding" ("Id");
            """
        );
    }

    [Fact]
    public async Task FreshDatabase_CreatesUsableIndexAndRecordsMigration()
    {
        await _context.Database.MigrateAsync(Token);

        Assert.Single(await _context.Database.GetAppliedMigrationsAsync(Token));
        Assert.True(
            (bool)
                await Scalar(
                    $"SELECT indisvalid AND indisready AND indislive FROM pg_index WHERE indexrelid='public.\"{Index}\"'::regclass"
                )
        );
        Assert.Equal(10L, await Scalar("SELECT count(*) FROM public.\"Embedding\""));
        Assert.Equal(
            "vector",
            await Scalar("SELECT pg_typeof(\"Vector\")::text FROM public.\"Embedding\" LIMIT 1")
        );
        Assert.NotEqual(
            DBNull.Value,
            await Scalar("SELECT to_regclass('public.\"ExistingEmbeddingIdIndex\"')::text")
        );
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(Token);
        await using var command = new NpgsqlCommand(
            """
            SET enable_seqscan = off;
            EXPLAIN SELECT "Id" FROM public."Embedding"
            WHERE "Model" = 'qwen3-embedding:0.6b' AND "VectorDimension" = 1024
            ORDER BY "Vector"::halfvec(1024) <=> ('[1' || repeat(',0',1023) || ']')::halfvec(1024) LIMIT 2;
            """,
            connection
        );
        await using var reader = await command.ExecuteReaderAsync(Token);
        var plan = new List<string>();
        while (await reader.ReadAsync(Token))
            plan.Add(reader.GetString(0));
        Assert.Contains(plan, line => line.Contains(Index, StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExistingValidIndex_IsAdoptedWithoutRebuildAndRetryIsNoOp()
    {
        await Execute(QwenHalfvecIndex20260930.CreateSql);
        var before = await IndexIdentity();

        await _context.Database.MigrateAsync(Token);
        await _context.Database.MigrateAsync(Token);

        Assert.Equal(before, await IndexIdentity());
        Assert.Single(await _context.Database.GetAppliedMigrationsAsync(Token));
    }

    [Theory]
    [InlineData("halfvec(1024)", "halfvec(512)")]
    [InlineData("halfvec_cosine_ops", "halfvec_l2_ops")]
    [InlineData("qwen3-embedding:0.6b", "other-model")]
    [InlineData("\"VectorDimension\" = 1024", "\"VectorDimension\" = 512")]
    [InlineData("m = 16", "m = 8")]
    [InlineData("ef_construction = 64", "ef_construction = 128")]
    public async Task MismatchedIndex_IsPreservedAndNeverRecorded(
        string original,
        string replacement
    )
    {
        // No rows need conversion when testing a deliberately incompatible expression.
        await Execute("TRUNCATE public.\"Embedding\"");
        await Execute(
            QwenHalfvecIndex20260930.CreateSql.Replace(
                original,
                replacement,
                StringComparison.Ordinal
            )
        );
        var before = await IndexIdentity();

        await Assert.ThrowsAsync<PostgresException>(() => _context.Database.MigrateAsync(Token));

        Assert.Equal(before, await IndexIdentity());
        Assert.Empty(await _context.Database.GetAppliedMigrationsAsync(Token));
    }

    [Theory]
    [InlineData("indisvalid")]
    [InlineData("indisready")]
    [InlineData("indislive")]
    public async Task UnfinishedIndex_IsPreservedAndNeverRecorded(string flag)
    {
        await Execute(QwenHalfvecIndex20260930.CreateSql);
        var before = await IndexIdentity();
        // Isolated disposable database: emulate each catalog state of an interrupted build.
        await Execute(
            $"UPDATE pg_index SET {flag}=false WHERE indexrelid='public.\"{Index}\"'::regclass"
        );

        await Assert.ThrowsAsync<PostgresException>(() => _context.Database.MigrateAsync(Token));

        Assert.Equal(before, await IndexIdentity());
        Assert.Empty(await _context.Database.GetAppliedMigrationsAsync(Token));
    }

    [Fact]
    public async Task NameCollisionWithTable_IsPreservedAndNeverRecorded()
    {
        await Execute($"CREATE TABLE public.\"{Index}\" (id integer)");

        await Assert.ThrowsAsync<PostgresException>(() => _context.Database.MigrateAsync(Token));

        Assert.Equal(
            "r",
            await Scalar(
                $"SELECT relkind::text FROM pg_class WHERE oid='public.\"{Index}\"'::regclass"
            )
        );
        Assert.Empty(await _context.Database.GetAppliedMigrationsAsync(Token));
    }

    [Fact]
    public async Task ManualBuildLock_RejectsMigrationUntilBuilderFinishes()
    {
        await using var builder = new NpgsqlConnection(_connectionString);
        await builder.OpenAsync(Token);
        await using var acquire = new NpgsqlCommand(
            "SELECT pg_advisory_lock(837421,1024)",
            builder
        );
        await acquire.ExecuteNonQueryAsync(Token);

        await Assert.ThrowsAsync<PostgresException>(() => _context.Database.MigrateAsync(Token));
        Assert.Empty(await _context.Database.GetAppliedMigrationsAsync(Token));
        Assert.Equal(DBNull.Value, await Scalar($"SELECT to_regclass('public.\"{Index}\"')::text"));

        await builder.CloseAsync();
        await _context.Database.MigrateAsync(Token);
        Assert.Single(await _context.Database.GetAppliedMigrationsAsync(Token));
    }

    [Fact]
    public async Task LargeTableWithoutIndex_RequiresPrebuildThenAdoptsIt()
    {
        // Null vectors keep the prebuild instant: the guard counts rows, not graph entries.
        await Execute(
            $"""
            TRUNCATE public."Embedding";
            INSERT INTO public."Embedding"
            SELECT n, NULL, 'qwen3-embedding:0.6b', 1024
            FROM generate_series(1, {QwenHalfvecIndex20260930.MaxInlineBuildRows + 1}) n;
            """
        );

        var refusal = await Assert.ThrowsAsync<PostgresException>(() =>
            _context.Database.MigrateAsync(Token)
        );

        Assert.Contains("too large", refusal.MessageText, StringComparison.Ordinal);
        Assert.Equal(DBNull.Value, await Scalar($"SELECT to_regclass('public.\"{Index}\"')::text"));
        Assert.Empty(await _context.Database.GetAppliedMigrationsAsync(Token));

        await Execute(QwenHalfvecIndex20260930.CreateSql);
        var before = await IndexIdentity();
        await _context.Database.MigrateAsync(Token);

        Assert.Equal(before, await IndexIdentity());
        Assert.Single(await _context.Database.GetAppliedMigrationsAsync(Token));
    }

    [Fact]
    public async Task TableAtInlineLimit_BuildsInsideMigration()
    {
        await Execute(
            $"""
            TRUNCATE public."Embedding";
            INSERT INTO public."Embedding"
            SELECT n, NULL, 'qwen3-embedding:0.6b', 1024
            FROM generate_series(1, {QwenHalfvecIndex20260930.MaxInlineBuildRows}) n;
            INSERT INTO public."Embedding"
            SELECT n, NULL, 'other-model', 1024
            FROM generate_series(200001, 200100) n;
            """
        );

        await _context.Database.MigrateAsync(Token);

        Assert.Single(await _context.Database.GetAppliedMigrationsAsync(Token));
    }

    [Theory]
    [InlineData("text")]
    [InlineData("varchar(64)")]
    public async Task ModelColumnType_DoesNotChangeTheDefinitionProof(string type)
    {
        await Execute($"ALTER TABLE public.\"Embedding\" ALTER COLUMN \"Model\" TYPE {type}");

        await _context.Database.MigrateAsync(Token);

        Assert.Single(await _context.Database.GetAppliedMigrationsAsync(Token));
    }

    [Fact]
    public async Task Rollback_RemovesInvalidLeftover()
    {
        await _context.Database.MigrateAsync(Token);
        // Isolated disposable database: emulate an index invalidated after it was recorded.
        await Execute(
            $"UPDATE pg_index SET indisvalid=false WHERE indexrelid='public.\"{Index}\"'::regclass"
        );

        await _context.GetService<IMigrator>().MigrateAsync(Migration.InitialDatabase, Token);

        Assert.Equal(DBNull.Value, await Scalar($"SELECT to_regclass('public.\"{Index}\"')::text"));
        Assert.Empty(await _context.Database.GetAppliedMigrationsAsync(Token));
    }

    [Fact]
    public async Task Rollback_IsRefusedWhileBuilderHoldsTheLock()
    {
        await _context.Database.MigrateAsync(Token);
        await using var builder = new NpgsqlConnection(_connectionString);
        await builder.OpenAsync(Token);
        await using var acquire = new NpgsqlCommand(
            "SELECT pg_advisory_lock(837421,1024)",
            builder
        );
        await acquire.ExecuteNonQueryAsync(Token);

        await Assert.ThrowsAsync<PostgresException>(() =>
            _context.GetService<IMigrator>().MigrateAsync(Migration.InitialDatabase, Token)
        );

        Assert.NotEqual(
            DBNull.Value,
            await Scalar($"SELECT to_regclass('public.\"{Index}\"')::text")
        );
        Assert.Single(await _context.Database.GetAppliedMigrationsAsync(Token));
    }

    [Fact]
    public async Task Rollback_RemovesOnlyThisIndexAndCanReapply()
    {
        await _context.Database.MigrateAsync(Token);
        await _context.GetService<IMigrator>().MigrateAsync(Migration.InitialDatabase, Token);
        Assert.Equal(DBNull.Value, await Scalar($"SELECT to_regclass('public.\"{Index}\"')::text"));
        Assert.NotEqual(
            DBNull.Value,
            await Scalar("SELECT to_regclass('public.\"ExistingEmbeddingIdIndex\"')::text")
        );
        Assert.Empty(await _context.Database.GetAppliedMigrationsAsync(Token));

        await _context.Database.MigrateAsync(Token);
        Assert.Single(await _context.Database.GetAppliedMigrationsAsync(Token));
        Assert.Equal(10L, await Scalar("SELECT count(*) FROM public.\"Embedding\""));
    }

    private Task<object> IndexIdentity() =>
        Scalar(
            $"SELECT oid::text || ':' || relfilenode::text FROM pg_class WHERE oid='public.\"{Index}\"'::regclass"
        );

    private Task Execute(string sql) => ExecuteOn(_connectionString, sql);

    private static async Task ExecuteOn(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(Token);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(Token);
    }

    private async Task<object> Scalar(string sql)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(Token);
        await using var command = new NpgsqlCommand(sql, connection);
        return await command.ExecuteScalarAsync(Token);
    }

    public async Task DisposeAsync()
    {
        if (_context != null)
            await _context.DisposeAsync();
        await ExecuteOn(
            fixture.Database.GetConnectionString(),
            $"DROP DATABASE IF EXISTS {_databaseName} WITH (FORCE)"
        );
    }
}

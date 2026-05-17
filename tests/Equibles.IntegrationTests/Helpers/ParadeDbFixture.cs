using Equibles.Cboe.Data;
using Equibles.Cftc.Data;
using Equibles.CommonStocks.Data;
using Equibles.Congress.Data;
using Equibles.Data;
using Equibles.Errors.Data;
using Equibles.Finra.Data;
using Equibles.Fred.Data;
using Equibles.Holdings.Data;
using Equibles.InsiderTrading.Data;
using Equibles.Media.Data;
using Equibles.Messaging;
using Equibles.ParadeDB.EntityFrameworkCore;
using Equibles.Sec.Data;
using Equibles.Yahoo.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Respawn;
using Testcontainers.PostgreSql;
using Xunit;

namespace Equibles.IntegrationTests.Helpers;

/// <summary>
/// Boots a ParadeDB (PostgreSQL + pgvector + pg_search) container once per xUnit collection,
/// applies the production EF Core migrations against it, and exposes helpers for tests to
/// pull a fresh <see cref="EquiblesDbContext"/> and truncate user data between tests via
/// Respawn.
///
/// MCP integration tests share a single container — boot cost (~30s on a cold pull,
/// migration apply is ~5-10s) is amortised across the suite. Each test calls
/// <see cref="ResetAsync"/> from its <see cref="IAsyncLifetime.InitializeAsync"/> to guarantee
/// a clean state without re-running migrations.
/// </summary>
public class ParadeDbFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("paradedb/paradedb:latest")
        .WithDatabase("equibles_integration")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    private Respawner _respawner;

    public string ConnectionString { get; private set; }

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        ConnectionString = _container.GetConnectionString();

        await using (var ctx = CreateDbContext())
        {
            // Production timeout: SetCommandTimeout for paranoid index rebuilds. Tests don't need
            // the hour-long ceiling, but a few minutes guards against a slow container on a
            // first-run cold start where Postgres is still warming up its shared buffers.
            ctx.Database.SetCommandTimeout(TimeSpan.FromMinutes(5));
            await ctx.Database.MigrateAsync();
        }

        // Respawn snapshots user tables once and replays TRUNCATE on every reset — far faster
        // than dropping/recreating schemas. Excluding __EFMigrationsHistory means the migration
        // state survives resets so we never re-apply migrations between tests.
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        _respawner = await Respawner.CreateAsync(
            connection,
            new RespawnerOptions
            {
                DbAdapter = DbAdapter.Postgres,
                SchemasToInclude = ["public"],
                TablesToIgnore = [new Respawn.Graph.Table("__EFMigrationsHistory")],
            }
        );
    }

    public async Task DisposeAsync()
    {
        await _container.DisposeAsync();
    }

    /// <summary>
    /// Returns a fresh, attached <see cref="EquiblesDbContext"/>. The caller owns disposal.
    /// Configurations mirror production: pgvector, ParadeDB, query splitting, lazy loading,
    /// and the migrations assembly so any future <c>MigrateAsync</c> call (e.g., reset)
    /// uses the same migration set.
    /// </summary>
    public EquiblesDbContext CreateDbContext()
    {
        var optionsBuilder = new DbContextOptionsBuilder<EquiblesDbContext>();
        optionsBuilder.UseNpgsql(
            ConnectionString,
            npgsql =>
            {
                npgsql.UseVector();
                npgsql.UseParadeDb();
                npgsql.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);
                npgsql.MigrationsAssembly(
                    typeof(Equibles.Migrations.DesignTimeDbContextFactory).Assembly
                );
            }
        );
        optionsBuilder.UseLazyLoadingProxies();

        IModuleConfiguration[] modules =
        [
            new CommonStocksModuleConfiguration(),
            new HoldingsModuleConfiguration(),
            new InsiderTradingModuleConfiguration(),
            new CongressModuleConfiguration(),
            new FinraModuleConfiguration(),
            new FredModuleConfiguration(),
            new YahooModuleConfiguration(),
            new CftcModuleConfiguration(),
            new CboeModuleConfiguration(),
            new SecModuleConfiguration(),
            new MediaModuleConfiguration(),
            new ErrorsModuleConfiguration(),
            new MessagingModuleConfiguration(),
        ];

        return new EquiblesDbContext(optionsBuilder.Options, modules);
    }

    /// <summary>
    /// Truncates every user table in <c>public</c> while keeping the schema and migration
    /// history intact. Call from a test's <c>InitializeAsync</c> so each test starts from
    /// an empty database without paying the migration cost again.
    /// </summary>
    public async Task ResetAsync()
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await _respawner.ResetAsync(connection);
    }
}

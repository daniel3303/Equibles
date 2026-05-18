using System.Globalization;
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
using Equibles.Mcp.Server;
using Equibles.Media.Data;
using Equibles.Messaging;
using Equibles.ParadeDB.EntityFrameworkCore;
using Equibles.Sec.Data;
using Equibles.Yahoo.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Respawn;
using Testcontainers.PostgreSql;
using Xunit;

namespace Equibles.FunctionalTests.Fixtures;

/// <summary>
/// Hosts the real Equibles.Mcp.Server on a Kestrel-bound random port, backed by a
/// Testcontainers ParadeDB instance. Mirrors <see cref="WebAppFixture"/> for the MCP server
/// composition — boots the host directly via <see cref="WebApplication.CreateBuilder"/>
/// rather than inheriting from <c>WebApplicationFactory&lt;T&gt;</c>, because
/// <c>WebApplicationFactory</c> swaps the server for <c>TestServer</c> and would prevent
/// the MCP client SDK from reaching a real HTTP listener over real network sockets.
///
/// The MCP server's pipeline mounts <c>ApiKeyMiddleware</c> in front of <c>/mcp</c>. Without
/// <c>McpApiKey</c> set, <c>SimpleApiKeyValidator.IsEnabled</c> is false and the middleware
/// short-circuits — keeping this fixture focused on the protocol surface. A separate fixture
/// could pin the authenticated path by setting <c>builder.Configuration["McpApiKey"]</c>
/// before <c>Program.ConfigureServices</c> runs.
/// </summary>
public class McpServerAppFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _db = new PostgreSqlBuilder()
        .WithImage("paradedb/paradedb:latest")
        .WithDatabase("equibles_functional_mcp")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    private WebApplication _app;
    private Respawner _respawner;

    public string BaseUrl { get; private set; }

    /// <summary>
    /// Application root services. Use a scope (<c>Services.CreateScope()</c>) before resolving
    /// scoped dependencies like <see cref="EquiblesDbContext"/>; never resolve them directly
    /// from this provider.
    /// </summary>
    public IServiceProvider Services => _app.Services;

    public async Task InitializeAsync()
    {
        // MCP tool output formats with :N0 / :F2 which honour CurrentCulture per-thread.
        // The Kestrel-served threads inherit DefaultThreadCurrentCulture; without pinning
        // invariant, dev/CI machines on non-en-US locales would render "175,50" / "50 000 000"
        // and break substring assertions that expect "175.50" / "50,000,000".
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;

        await _db.StartAsync();

        // The MCP server's AddEquiblesDbContext doesn't configure a MigrationsAssembly —
        // production assumes another service (the Web app) has already migrated the shared
        // database. For the test fixture, apply migrations explicitly via a separate
        // DbContext that knows about Equibles.Migrations.DesignTimeDbContextFactory's
        // assembly, mirroring the ParadeDbFixture pattern used by the integration tests.
        await using (var migrationContext = BuildMigrationContext(_db.GetConnectionString()))
        {
            migrationContext.Database.SetCommandTimeout(TimeSpan.FromMinutes(5));
            await migrationContext.Database.MigrateAsync();
        }

        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions
            {
                ApplicationName = "Equibles.Mcp.Server",
                ContentRootPath = ResolveMcpServerContentRoot(),
                EnvironmentName = "Production",
            }
        );

        builder.Configuration["ConnectionStrings:DefaultConnection"] = _db.GetConnectionString();

        // Production MCP server doesn't opt in to validate-on-build; EmbeddingClient pulls
        // IHttpClientFactory lazily on first use and isn't registered at composition time.
        // Match that behaviour so the fixture's host build doesn't reject the composition
        // before any request is served.
        builder.Host.UseDefaultServiceProvider(o =>
        {
            o.ValidateOnBuild = false;
            o.ValidateScopes = false;
        });

        Program.ConfigureServices(builder);
        _app = builder.Build();
        Program.ConfigurePipeline(_app);

        _app.Urls.Add("http://127.0.0.1:0");
        await _app.StartAsync();

        // _app.Urls.First() returns the literally-configured URL ("...:0") rather than the
        // OS-assigned port. Pull the resolved bound address from IServerAddressesFeature so
        // tests can connect to the actual listener.
        var addresses = _app
            .Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>();
        BaseUrl = addresses.Addresses.First();

        // Snapshot user tables for Respawn to replay TRUNCATE on every reset. Excludes the
        // EF Core migrations history so tests don't re-apply migrations between resets.
        // Same Postgres caveat as WebAppFixture: Respawn's string overload defaults to
        // SqlClient, so pass an explicit NpgsqlConnection.
        await using var respawnConnection = new NpgsqlConnection(_db.GetConnectionString());
        await respawnConnection.OpenAsync();
        _respawner = await Respawner.CreateAsync(
            respawnConnection,
            new RespawnerOptions
            {
                DbAdapter = DbAdapter.Postgres,
                SchemasToInclude = ["public"],
                TablesToIgnore = [new Respawn.Graph.Table("__EFMigrationsHistory")],
            }
        );
    }

    /// <summary>
    /// Truncates every user table in <c>public</c> via Respawn, then runs <paramref name="seed"/>
    /// against a fresh <see cref="EquiblesDbContext"/> scope. The seed delegate receives an
    /// attached DbContext — add entities and the method will call <c>SaveChangesAsync</c> for
    /// you. Pass <c>null</c> (or omit) to reset without seeding.
    ///
    /// Call this from a test's <c>InitializeAsync</c> so each test starts from a known state.
    /// xUnit creates a fresh test class instance per test, so per-test isolation is automatic.
    /// </summary>
    public async Task ResetAndSeedAsync(Func<EquiblesDbContext, Task> seed = null)
    {
        await using (var resetConnection = new NpgsqlConnection(_db.GetConnectionString()))
        {
            await resetConnection.OpenAsync();
            await _respawner.ResetAsync(resetConnection);
        }

        if (seed is null)
            return;

        using var scope = _app.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<EquiblesDbContext>();
        await seed(dbContext);
        await dbContext.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
        await _db.DisposeAsync();
    }

    private static EquiblesDbContext BuildMigrationContext(string connectionString)
    {
        var optionsBuilder = new DbContextOptionsBuilder<EquiblesDbContext>();
        optionsBuilder.UseNpgsql(
            connectionString,
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

    private static string ResolveMcpServerContentRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Equibles.sln")))
        {
            dir = dir.Parent;
        }
        if (dir is null)
        {
            throw new InvalidOperationException(
                "Could not locate Equibles.sln from test bin directory — fixture cannot resolve ContentRootPath."
            );
        }
        return Path.Combine(dir.FullName, "src", "Equibles.Mcp.Server");
    }
}

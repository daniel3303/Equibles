using Equibles.Data;
using Equibles.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Respawn;
using Testcontainers.PostgreSql;
using Xunit;

namespace Equibles.FunctionalTests.Fixtures;

/// <summary>
/// Hosts the real Equibles.Web app on a Kestrel-bound random port, backed by a Testcontainers
/// ParadeDB instance (PostgreSQL + pgvector + pg_search) so EF Core migrations apply against
/// the same engine as production. One container per fixture; shared across the whole functional
/// collection to amortise the ~15-30s container boot cost.
///
/// Builds the host directly via <see cref="WebApplication.CreateBuilder()"/> rather than
/// inheriting from <c>WebApplicationFactory&lt;T&gt;</c>. <c>WebApplicationFactory</c> hardcodes
/// its IServer as a <c>TestServer</c> — accessing its <c>Server</c> or <c>Services</c> property
/// throws <c>InvalidCastException</c> when the underlying server is Kestrel, so it cannot drive
/// the real HTTP listener Playwright needs.
///
/// Tests that need pre-existing data call <see cref="ResetAndSeedAsync"/> from their
/// constructor — xUnit creates a fresh test class instance per test, so per-test isolation
/// is automatic. Respawn truncates every user table in <c>public</c> between calls while
/// keeping the migration history intact.
/// </summary>
public class WebAppFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _db = new PostgreSqlBuilder()
        .WithImage("paradedb/paradedb:latest")
        .WithDatabase("equibles_functional")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    private WebApplication _app;
    private string _keysDirectory;
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
        await _db.StartAsync();

        // Per-fixture ephemeral keys directory. The production default (/app/keys) doesn't exist
        // on dev/CI hosts and would crash AddDataProtection at startup.
        _keysDirectory = Path.Combine(
            Path.GetTempPath(),
            $"equibles-functional-keys-{Guid.NewGuid():N}"
        );
        Directory.CreateDirectory(_keysDirectory);

        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions
            {
                ApplicationName = "Equibles.Web",
                // Content root must point at the Web project's source dir so Views/, wwwroot/, and
                // AddRazorRuntimeCompilation can find their files relative to it.
                ContentRootPath = ResolveWebContentRoot(),
                EnvironmentName = "Development",
            }
        );

        builder.Configuration["ConnectionStrings:DefaultConnection"] = _db.GetConnectionString();
        // AddMessaging binds MassTransit's SQL transport from this string. Same
        // container as the app DB to mirror docker-compose, where web and worker
        // share Postgres.
        builder.Configuration["ConnectionStrings:TransportConnection"] = _db.GetConnectionString();
        builder.Configuration["DataProtection:KeysDirectory"] = _keysDirectory;

        Program.ConfigureServices(builder);
        _app = builder.Build();
        await Program.ApplyMigrationsAsync(_app);
        Program.ConfigurePipeline(_app);

        _app.Urls.Add("http://127.0.0.1:0");
        await _app.StartAsync();
        BaseUrl = _app.Urls.First();

        // Respawn snapshots the schema's user tables once and replays a TRUNCATE on every reset.
        // Limit to the public schema so ParadeDB's pg_search / pgvector internal tables are not
        // touched. The migrations history table is excluded so EF Core doesn't replay migrations
        // on every test. Respawn's string overload defaults to SqlClient — for Postgres we open
        // an explicit NpgsqlConnection and pass it through.
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

    public async Task DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
        await _db.DisposeAsync();
        if (_keysDirectory is not null && Directory.Exists(_keysDirectory))
        {
            try
            {
                Directory.Delete(_keysDirectory, recursive: true);
            }
            catch
            { /* best-effort */
            }
        }
    }

    /// <summary>
    /// Truncates all user tables in <c>public</c> via Respawn, then runs <paramref name="seed"/>
    /// against a fresh <see cref="EquiblesDbContext"/> scope. The seed delegate receives an
    /// attached DbContext — add entities and the method will call <c>SaveChangesAsync</c> for
    /// you. Pass <c>null</c> (or omit) to reset without seeding.
    ///
    /// Call this from the test class constructor so each test starts from a known state. xUnit
    /// instantiates the class once per test, so seeding does not leak across tests in the same
    /// class.
    /// </summary>
    public async Task ResetAndSeedAsync(Func<EquiblesDbContext, Task> seed = null)
    {
        // Same Postgres caveat as fixture init — Respawn's string overload defaults to SqlClient.
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

    private static string ResolveWebContentRoot()
    {
        // Walk up from the test bin directory until we find Equibles.sln, then resolve the Web
        // project's source root. Works under both `dotnet test` and `dotnet test --output …`.
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
        return Path.Combine(dir.FullName, "src", "Equibles.Web");
    }
}

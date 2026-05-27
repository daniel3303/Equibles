using Equibles.Data;
using Equibles.ParadeDB.EntityFrameworkCore;
using Equibles.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Respawn;
using Testcontainers.PostgreSql;
using Xunit;

namespace Equibles.IntegrationTests.Helpers;

/// <summary>
/// In-process integration host for the real Equibles.Web app. Controller and tab
/// service tests instantiate controllers directly, so no test exercises the Razor
/// view pipeline — every compiled <c>AspNetCoreGeneratedDocument.Views_*</c> class
/// is uncovered. This fixture boots the production host on a Kestrel loopback port
/// backed by a Testcontainers ParadeDB instance and exposes an
/// <see cref="HttpClient"/>, so a GET drives routing → controller → Razor view in
/// the test process where coverage is captured.
///
/// Mirrors the functional <c>WebAppFixture</c> design (manual host build rather
/// than <c>WebApplicationFactory&lt;T&gt;</c>, whose hardcoded TestServer and
/// <c>DisposeAsync</c> signature clash with xUnit v2's <see cref="IAsyncLifetime"/>).
/// One container per collection amortises the boot cost.
/// </summary>
public class WebHostFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _db = new PostgreSqlBuilder()
        .WithImage("paradedb/paradedb:latest")
        .WithDatabase("equibles_webint")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    private WebApplication _app;
    private string _keysDirectory;
    private Respawner _respawner;

    public HttpClient Client { get; private set; }

    /// <summary>
    /// Application root services. Use a scope (<c>Services.CreateScope()</c>)
    /// before resolving scoped dependencies; never resolve them directly.
    /// </summary>
    public IServiceProvider Services => _app.Services;

    public async Task InitializeAsync()
    {
        await _db.StartAsync();

        // Production default (/app/keys) doesn't exist on dev/CI hosts and would
        // crash AddDataProtection at startup.
        _keysDirectory = Path.Combine(
            Path.GetTempPath(),
            $"equibles-webint-keys-{Guid.NewGuid():N}"
        );
        Directory.CreateDirectory(_keysDirectory);

        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions
            {
                ApplicationName = "Equibles.Web",
                ContentRootPath = ResolveWebContentRoot(),
                EnvironmentName = "Development",
            }
        );

        builder.Configuration["ConnectionStrings:DefaultConnection"] = _db.GetConnectionString();
        // AddMessaging binds MassTransit's SQL transport from this string. Same
        // container as the app DB to mirror docker-compose, where web and worker
        // share Postgres.
        builder.Configuration["ConnectionStrings:TransportConnection"] = _db.GetConnectionString();
        // No worker in integration tests, so the web host creates the transport
        // schema itself. Without this the bus fails its health check on first
        // start and any test that exercises /healthz returns 503.
        builder.Configuration["MassTransit:RunMigration"] = "true";
        builder.Configuration["DataProtection:KeysDirectory"] = _keysDirectory;

        Program.ConfigureServices(builder);

        // Test-host relaxation: the Web module composition currently has EF model
        // drift vs the migrations snapshot, so EF Core 9+ aborts MigrateAsync with
        // PendingModelChangesWarning (thrown by default). Re-register the context
        // ignoring only that warning so the schema still applies and views render.
        // (Flagged for maintainers — the deployed Web host hits the same path.)
        builder.Services.AddDbContext<EquiblesFinancialDbContext>(
            (sp, options) =>
            {
                options.UseNpgsql(
                    _db.GetConnectionString(),
                    npgsql =>
                        npgsql
                            .UseVector()
                            .UseParadeDb()
                            .UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery)
                );
                options.UseLazyLoadingProxies();
                options.ConfigureWarnings(w =>
                    w.Ignore(RelationalEventId.PendingModelChangesWarning)
                );
            }
        );

        _app = builder.Build();
        await Program.ApplyMigrationsAsync(_app);
        Program.ConfigurePipeline(_app);

        _app.Urls.Add("http://127.0.0.1:0");
        await _app.StartAsync();
        Client = new HttpClient { BaseAddress = new Uri(_app.Urls.First()) };

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
        Client?.Dispose();
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
    /// Truncates all user tables in <c>public</c>, then runs <paramref name="seed"/>
    /// against a fresh attached <see cref="EquiblesFinancialDbContext"/>. Call from the test
    /// class constructor — xUnit instantiates the class once per test, so seeded
    /// state never leaks across tests in the same class.
    /// </summary>
    public async Task ResetAndSeedAsync(Func<EquiblesFinancialDbContext, Task> seed = null)
    {
        await using (var resetConnection = new NpgsqlConnection(_db.GetConnectionString()))
        {
            await resetConnection.OpenAsync();
            await _respawner.ResetAsync(resetConnection);
        }

        if (seed is null)
            return;

        using var scope = _app.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<EquiblesFinancialDbContext>();
        await seed(dbContext);
        await dbContext.SaveChangesAsync();
    }

    private static string ResolveWebContentRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Equibles.sln")))
        {
            dir = dir.Parent;
        }
        if (dir is null)
        {
            throw new InvalidOperationException(
                "Could not locate Equibles.sln from test bin directory — cannot resolve ContentRootPath."
            );
        }
        return Path.Combine(dir.FullName, "src", "Equibles.Web");
    }
}

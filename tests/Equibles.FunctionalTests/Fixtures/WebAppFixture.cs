using Equibles.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
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
/// </summary>
public class WebAppFixture : IAsyncLifetime {
    private readonly PostgreSqlContainer _db = new PostgreSqlBuilder()
        .WithImage("paradedb/paradedb:latest")
        .WithDatabase("equibles_functional")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    private WebApplication _app;
    private string _keysDirectory;

    public string BaseUrl { get; private set; }

    public async Task InitializeAsync() {
        await _db.StartAsync();

        // Per-fixture ephemeral keys directory. The production default (/app/keys) doesn't exist
        // on dev/CI hosts and would crash AddDataProtection at startup.
        _keysDirectory = Path.Combine(Path.GetTempPath(), $"equibles-functional-keys-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_keysDirectory);

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions {
            ApplicationName = "Equibles.Web",
            // Content root must point at the Web project's source dir so Views/, wwwroot/, and
            // AddRazorRuntimeCompilation can find their files relative to it.
            ContentRootPath = ResolveWebContentRoot(),
            EnvironmentName = "Development",
        });

        builder.Configuration["ConnectionStrings:DefaultConnection"] = _db.GetConnectionString();
        builder.Configuration["DataProtection:KeysDirectory"] = _keysDirectory;

        Program.ConfigureServices(builder);
        _app = builder.Build();
        await Program.ApplyMigrationsAsync(_app);
        Program.ConfigurePipeline(_app);

        _app.Urls.Add("http://127.0.0.1:0");
        await _app.StartAsync();
        BaseUrl = _app.Urls.First();
    }

    public async Task DisposeAsync() {
        if (_app is not null) {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
        await _db.DisposeAsync();
        if (_keysDirectory is not null && Directory.Exists(_keysDirectory)) {
            try { Directory.Delete(_keysDirectory, recursive: true); } catch { /* best-effort */ }
        }
    }

    private static string ResolveWebContentRoot() {
        // Walk up from the test bin directory until we find Equibles.sln, then resolve the Web
        // project's source root. Works under both `dotnet test` and `dotnet test --output …`.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Equibles.sln"))) {
            dir = dir.Parent;
        }
        if (dir is null) {
            throw new InvalidOperationException(
                "Could not locate Equibles.sln from test bin directory — fixture cannot resolve ContentRootPath.");
        }
        return Path.Combine(dir.FullName, "src", "Equibles.Web");
    }
}

using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Testcontainers.PostgreSql;
using Xunit;

namespace Equibles.FunctionalTests.Fixtures;

/// <summary>
/// Hosts the real Equibles.Web app on a Kestrel-bound random port, backed by a Testcontainers
/// ParadeDB instance (PostgreSQL + pgvector + pg_search) so EF Core migrations apply against
/// the same engine as production. One container per fixture; shared across the whole functional
/// collection to amortise the ~15-30s container boot cost.
/// </summary>
public class WebAppFixture : WebApplicationFactory<Equibles.Web.Program>, IAsyncLifetime {
    // ParadeDB ships PostgreSQL with both `pg_search` and `vector` extensions, which the
    // initial migration declares via Npgsql:PostgresExtension annotations. Plain postgres images
    // don't have these and the migration fails on first run.
    private readonly PostgreSqlContainer _db = new PostgreSqlBuilder()
        .WithImage("paradedb/paradedb:latest")
        .WithDatabase("equibles_functional")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    public string BaseUrl { get; private set; }

    public async Task InitializeAsync() {
        await _db.StartAsync();
        // Trigger app build so WebApplicationFactory boots Kestrel and runs migrations.
        // Reading Server.Features pins the bound URL after Kestrel has chosen its port.
        var _ = Server;
        var addresses = Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>();
        BaseUrl = addresses?.Addresses.FirstOrDefault() ?? throw new InvalidOperationException(
            "Kestrel did not expose a bound address — IServerAddressesFeature returned no entries.");
    }

    public new async Task DisposeAsync() {
        await base.DisposeAsync();
        await _db.DisposeAsync();
    }

    protected override IHost CreateHost(IHostBuilder builder) {
        // Force Kestrel onto a random loopback port. WebApplicationFactory's default in-memory
        // TestServer can't accept browser traffic; Playwright drives a real browser, so we
        // need a real socket.
        builder.ConfigureWebHost(webHost => {
            webHost.UseUrls("http://127.0.0.1:0");
            webHost.UseKestrel();
        });
        return base.CreateHost(builder);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder) {
        // Override the production DefaultConnection so EF Core points at the test container.
        builder.UseSetting("ConnectionStrings:DefaultConnection", _db.GetConnectionString());

        // Production Program.cs writes data-protection keys to /app/keys (the container path).
        // On dev/CI hosts that directory doesn't exist; override to a temp directory per fixture.
        var keysDir = Path.Combine(Path.GetTempPath(), $"equibles-functional-keys-{Guid.NewGuid():N}");
        Directory.CreateDirectory(keysDir);
        builder.UseSetting("DataProtection:KeysDirectory", keysDir);

        builder.UseEnvironment("Development");
    }
}

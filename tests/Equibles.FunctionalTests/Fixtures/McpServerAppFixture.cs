using Equibles.Data;
using Equibles.Mcp.Server;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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
public class McpServerAppFixture : IAsyncLifetime {
    private readonly PostgreSqlContainer _db = new PostgreSqlBuilder()
        .WithImage("paradedb/paradedb:latest")
        .WithDatabase("equibles_functional_mcp")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    private WebApplication _app;

    public string BaseUrl { get; private set; }

    public async Task InitializeAsync() {
        await _db.StartAsync();

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions {
            ApplicationName = "Equibles.Mcp.Server",
            ContentRootPath = ResolveMcpServerContentRoot(),
            EnvironmentName = "Production",
        });

        builder.Configuration["ConnectionStrings:DefaultConnection"] = _db.GetConnectionString();

        // Production MCP server doesn't opt in to validate-on-build; EmbeddingClient pulls
        // IHttpClientFactory lazily on first use and isn't registered at composition time.
        // Match that behaviour so the fixture's host build doesn't reject the composition
        // before any request is served.
        builder.Host.UseDefaultServiceProvider(o => {
            o.ValidateOnBuild = false;
            o.ValidateScopes = false;
        });

        Program.ConfigureServices(builder);
        _app = builder.Build();
        await Program.ApplyMigrationsAsync(_app);
        Program.ConfigurePipeline(_app);

        _app.Urls.Add("http://127.0.0.1:0");
        await _app.StartAsync();

        // _app.Urls.First() returns the literally-configured URL ("...:0") rather than the
        // OS-assigned port. Pull the resolved bound address from IServerAddressesFeature so
        // tests can connect to the actual listener.
        var addresses = _app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>();
        BaseUrl = addresses.Addresses.First();
    }

    public async Task DisposeAsync() {
        if (_app is not null) {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
        await _db.DisposeAsync();
    }

    private static string ResolveMcpServerContentRoot() {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Equibles.sln"))) {
            dir = dir.Parent;
        }
        if (dir is null) {
            throw new InvalidOperationException(
                "Could not locate Equibles.sln from test bin directory — fixture cannot resolve ContentRootPath.");
        }
        return Path.Combine(dir.FullName, "src", "Equibles.Mcp.Server");
    }
}

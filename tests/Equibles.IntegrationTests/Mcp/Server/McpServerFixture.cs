using Equibles.IntegrationTests.Helpers;
using Equibles.Mcp.Server;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Equibles.IntegrationTests.Mcp.Server;

/// <summary>
/// Boots the full Equibles MCP Server in-process via <see cref="WebApplicationFactory{T}"/>,
/// backed by the shared <see cref="ParadeDbFixture"/> ParadeDB container so EF Core registers
/// against the same engine as production. Exposes an <see cref="HttpClient"/> wired to the
/// TestServer pipeline that integration tests can hand to the MCP client SDK's
/// <c>HttpClientTransport</c>.
///
/// The MCP Server's <c>ConfigureServices</c> resolves the connection string from configuration,
/// so the fixture overrides <c>ConnectionStrings:DefaultConnection</c> at WebHost build time
/// to point at the Testcontainers ParadeDB instance.
/// </summary>
public class McpServerFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly ParadeDbFixture _paradeDb;

    public McpServerFixture(ParadeDbFixture paradeDb)
    {
        _paradeDb = paradeDb;
    }

    public string ConnectionString => _paradeDb.ConnectionString;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:DefaultConnection", _paradeDb.ConnectionString);
        builder.UseSetting("Embedding:Enabled", "false");
    }

    public async Task InitializeAsync()
    {
        // Materialise the TestServer eagerly so any startup exception surfaces here rather
        // than in the first test. CreateClient() triggers host build, which runs
        // ConfigureServices/ConfigurePipeline against the real MCP server code path.
        _ = CreateClient();
        await Task.CompletedTask;
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await DisposeAsync();
    }
}

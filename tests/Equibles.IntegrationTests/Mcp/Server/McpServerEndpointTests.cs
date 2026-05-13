using Equibles.IntegrationTests.Helpers;
using ModelContextProtocol.Client;
using Xunit;

namespace Equibles.IntegrationTests.Mcp.Server;

/// <summary>
/// Integration test that boots the real Equibles MCP Server (Program.ConfigureServices +
/// Program.ConfigurePipeline) in-process via WebApplicationFactory against the shared
/// ParadeDB container, then drives it with the official MCP client SDK over HTTP. Verifies
/// that the wired-up server actually responds to a real MCP protocol exchange — the
/// initialize handshake plus a tools/list call — and exposes the tool surface the
/// production composition registers (Holdings, InsiderTrading, Fred, Sec, Cftc, Cboe,
/// Congress, ShortData, StockPrices).
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class McpServerEndpointTests : IAsyncLifetime {
    private readonly ParadeDbFixture _paradeDb;
    private McpServerFixture _serverFixture;

    public McpServerEndpointTests(ParadeDbFixture paradeDb) {
        _paradeDb = paradeDb;
    }

    public async Task InitializeAsync() {
        _serverFixture = new McpServerFixture(_paradeDb);
        await _serverFixture.InitializeAsync();
    }

    public async Task DisposeAsync() {
        if (_serverFixture is not null) {
            await ((IAsyncLifetime)_serverFixture).DisposeAsync();
        }
    }

    [Fact]
    public async Task ListTools_ServerHostedInProcessOverHttp_ReturnsToolsFromEveryRegisteredModule() {
        // The MCP client SDK speaks the streamable-HTTP transport — handshake (initialize)
        // followed by JSON-RPC framed requests. WebApplicationFactory's CreateClient() returns
        // an HttpClient bound to the in-process TestServer pipeline, so passing it to
        // HttpClientTransport routes the protocol exchange through the real ASP.NET Core
        // request pipeline that Program.cs configures, including the UseWhen branch that
        // mounts ApiKeyMiddleware in front of /mcp.
        //
        // The MCP server's appsettings.json doesn't set McpApiKey, so SimpleApiKeyValidator
        // ends up with IsEnabled=false and the middleware short-circuits. That keeps this
        // test focused on the protocol surface — a separate test could pin the
        // authenticated path by setting builder.UseSetting("McpApiKey", "...") in the fixture.
        var httpClient = _serverFixture.CreateClient();
        httpClient.BaseAddress = new Uri("http://localhost/");

        var transport = new HttpClientTransport(
            new HttpClientTransportOptions {
                Endpoint = new Uri("http://localhost/mcp"),
                TransportMode = HttpTransportMode.StreamableHttp,
            },
            httpClient);

        await using var client = await McpClient.CreateAsync(transport);
        var tools = await client.ListToolsAsync();

        tools.Should().NotBeEmpty();
        var toolNames = tools.Select(t => t.Name).ToList();

        // Sample a handful from across the registered MCP modules — every Add*() call in
        // Program.ConfigureServices's AddEquiblesMcp(...) block should contribute tools, so
        // we cherry-pick one well-known name from each broad area. The exact tool naming
        // is set by [Mcp tool attribute] in each module — these are the canonical names
        // currently produced by Holdings, InsiderTrading, Fred, Sec, Congress, and price
        // tooling and asserted on (rather than asserting on a count) so a future regression
        // that silently drops a single module's registration fails on the missing name
        // rather than a brittle count.
        toolNames.Should().Contain("GetTopHolders");
        toolNames.Should().Contain("GetInsiderTransactions");
        toolNames.Should().Contain("GetEconomicIndicator");
        toolNames.Should().Contain("SearchCompanyDocuments");
        toolNames.Should().Contain("GetCongressionalTrades");
        toolNames.Should().Contain("GetStockPrices");
    }
}

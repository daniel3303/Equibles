using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Equibles.IntegrationTests.Helpers;
using Microsoft.AspNetCore.Hosting;
using ModelContextProtocol.Client;
using Xunit;

namespace Equibles.IntegrationTests.Mcp.Server;

/// <summary>
/// Pins the authenticated side of the MCP server pipeline that
/// <see cref="McpServerEndpointTests"/> deliberately skips — that test ran with the
/// default appsettings (no <c>McpApiKey</c>), which short-circuits
/// <see cref="Equibles.Mcp.Middleware.ApiKeyMiddleware"/> via
/// <see cref="Equibles.Mcp.Server.SimpleApiKeyValidator.IsEnabled"/>=<c>false</c>.
/// With the key configured, the middleware is the only thing standing between an
/// anonymous caller and the MCP tool surface, so the production composition has to
/// satisfy both halves of the gate end-to-end: anonymous /mcp requests get a 401
/// before any JSON-RPC framing runs, AND a Bearer-authenticated MCP client completes
/// the handshake and reaches tools/list. A regression that drops the middleware from
/// <c>Program.ConfigurePipeline</c> trips the first assertion; one that wires it
/// without honouring the configured key trips the second.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class McpServerApiKeyAuthTests : IAsyncLifetime
{
    private const string ApiKey = "integration-test-mcp-api-key";

    private readonly ParadeDbFixture _paradeDb;
    private AuthenticatedMcpServerFixture _serverFixture;

    public McpServerApiKeyAuthTests(ParadeDbFixture paradeDb)
    {
        _paradeDb = paradeDb;
    }

    public async Task InitializeAsync()
    {
        _serverFixture = new AuthenticatedMcpServerFixture(_paradeDb, ApiKey);
        await _serverFixture.InitializeAsync();
    }

    public async Task DisposeAsync()
    {
        if (_serverFixture is not null)
        {
            await ((IAsyncLifetime)_serverFixture).DisposeAsync();
        }
    }

    [Fact]
    public async Task McpEndpoint_WithApiKeyConfigured_RejectsAnonymousAndAcceptsBearerAuthenticated()
    {
        // Anonymous request: ApiKeyMiddleware must reject before MCP layer sees the body. We
        // POST a non-empty payload so a "no body / framing error" inside the MCP transport
        // can't be confused with the 401 the gate is supposed to produce.
        var anonymousClient = _serverFixture.CreateClient();
        anonymousClient.BaseAddress = new Uri("http://localhost/");

        using var anonResponse = await anonymousClient.PostAsync(
            "/mcp",
            new StringContent("{}", Encoding.UTF8, "application/json")
        );

        anonResponse
            .StatusCode.Should()
            .Be(
                HttpStatusCode.Unauthorized,
                "ApiKeyMiddleware must guard /mcp when McpApiKey is configured"
            );

        // Authenticated request: real MCP client handshake + tools/list must succeed. The
        // SDK's HttpClientTransport reuses whatever HttpClient we hand it, so a default
        // Authorization header carries through every JSON-RPC POST it issues.
        var authClient = _serverFixture.CreateClient();
        authClient.BaseAddress = new Uri("http://localhost/");
        authClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            ApiKey
        );

        var transport = new HttpClientTransport(
            new HttpClientTransportOptions
            {
                Endpoint = new Uri("http://localhost/mcp"),
                TransportMode = HttpTransportMode.StreamableHttp,
            },
            authClient
        );

        await using var client = await McpClient.CreateAsync(transport);
        var tools = await client.ListToolsAsync();

        tools
            .Should()
            .NotBeEmpty(
                "Bearer-authenticated requests must reach the MCP protocol layer and enumerate registered tools"
            );
    }
}

internal class AuthenticatedMcpServerFixture : McpServerFixture
{
    private readonly string _apiKey;

    public AuthenticatedMcpServerFixture(ParadeDbFixture paradeDb, string apiKey)
        : base(paradeDb)
    {
        _apiKey = apiKey;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        // SimpleApiKeyValidator reads "McpApiKey" from IConfiguration at construction;
        // setting it via UseSetting before the host is built mirrors the production
        // environment-variable / appsettings.json path.
        builder.UseSetting("McpApiKey", _apiKey);
    }
}

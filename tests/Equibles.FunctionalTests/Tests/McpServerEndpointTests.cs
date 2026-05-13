using Equibles.FunctionalTests.Fixtures;
using FluentAssertions;
using ModelContextProtocol.Client;
using Xunit;

namespace Equibles.FunctionalTests.Tests;

/// <summary>
/// End-to-end functional test for the Equibles MCP Server: boots the real
/// <c>Equibles.Mcp.Server.Program</c> on a Kestrel-bound random port (no TestServer),
/// backed by a real Testcontainers ParadeDB, and drives it with the official MCP client
/// SDK over real HTTP. Differs from the parallel integration test in two important ways:
/// the request traverses an actual TCP socket and the full Kestrel response pipeline
/// (including content-type negotiation for the streamable-HTTP transport), and the
/// fixture applies EF Core migrations via the production <c>ApplyMigrationsAsync</c>
/// helper so the host build path matches what runs in <c>docker compose</c>.
/// </summary>
[Trait("Category", "Functional")]
public class McpServerEndpointTests : IClassFixture<McpServerAppFixture> {
    private readonly McpServerAppFixture _fixture;

    public McpServerEndpointTests(McpServerAppFixture fixture) {
        _fixture = fixture;
    }

    [Fact]
    public async Task ListTools_KestrelHostedMcpServer_ReturnsToolsViaRealHttpExchange() {
        var transport = new HttpClientTransport(new HttpClientTransportOptions {
            Endpoint = new Uri($"{_fixture.BaseUrl.TrimEnd('/')}/mcp"),
            TransportMode = HttpTransportMode.StreamableHttp,
        });

        await using var client = await McpClient.CreateAsync(transport);
        var tools = await client.ListToolsAsync();

        tools.Should().NotBeEmpty();
        var toolNames = tools.Select(t => t.Name).ToList();
        toolNames.Should().Contain("GetTopHolders");
        toolNames.Should().Contain("GetInsiderTransactions");
        toolNames.Should().Contain("GetStockPrices");
    }
}

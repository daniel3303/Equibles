using Equibles.FunctionalTests.Fixtures;
using FluentAssertions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
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

    [Fact]
    public async Task CallTool_GetTopHoldersForUnseededTicker_ReturnsTextContentViaToolsCallTransport() {
        // The sibling test proves tools/list works. This one drives the SECOND half of the MCP
        // protocol surface: tools/call. Without this, we'd be claiming "MCP works" while
        // having only verified that the server can advertise its tool list — the actual
        // tool invocation path (MCP framework → tool method dispatch → response framing)
        // would be completely untested via the transport.
        //
        // GetTopHolders is chosen because it has the smallest stable input surface (one
        // string ticker), returns a TextContent block, and returns a clean human-readable
        // error string when the stock doesn't exist in the DB rather than throwing —
        // exactly the property this test needs. The Testcontainers ParadeDB is freshly
        // migrated but empty, so the call lands on the "stock not found" branch.
        //
        // The assertion: tool is callable AND returns at least one TextContentBlock with
        // non-empty text. We don't pin the exact wording (that's the tool's contract,
        // covered by InstitutionalHoldingsToolsTests directly); we pin that the MCP
        // transport delivered the call to the tool and the tool's response made it back
        // through the framework as a content block. A regression in the MCP wire-up
        // (wrong serializer, missing tool registration, broken middleware) would fail
        // here even though tools/list would still succeed.
        var transport = new HttpClientTransport(new HttpClientTransportOptions {
            Endpoint = new Uri($"{_fixture.BaseUrl.TrimEnd('/')}/mcp"),
            TransportMode = HttpTransportMode.StreamableHttp,
        });

        await using var client = await McpClient.CreateAsync(transport);
        var tools = await client.ListToolsAsync();
        var getTopHolders = tools.First(t => t.Name == "GetTopHolders");

        var result = await getTopHolders.CallAsync(new Dictionary<string, object> {
            ["ticker"] = "UNSEEDED_TICKER"
        });

        result.IsError.Should().NotBe(true,
            "the tool returns a human-readable miss for unknown tickers, not a JSON-RPC error");
        var textBlocks = result.Content.OfType<TextContentBlock>().ToList();
        textBlocks.Should().NotBeEmpty("the tool must respond through the MCP transport as TextContent");
        textBlocks[0].Text.Should().NotBeNullOrWhiteSpace();
    }
}

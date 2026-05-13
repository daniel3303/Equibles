using System.Globalization;
using Equibles.Cboe.Data.Models;
using Equibles.IntegrationTests.Helpers;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace Equibles.IntegrationTests.Mcp.Server;

/// <summary>
/// <see cref="McpServerEndpointTests"/> only exercises <c>tools/list</c>; the MCP framework's
/// dispatch path for <c>tools/call</c> — parameter binding from JSON-RPC arguments to a
/// <c>[McpServerTool]</c>-decorated method, the production DI resolving the tool's
/// repositories against the live <see cref="EquiblesDbContext"/>, and the wrapping of the
/// tool's <see cref="string"/> return into a <see cref="TextContentBlock"/> — is the part
/// that can silently break when the SDK is upgraded or a module registration is shuffled.
///
/// This test fills that gap: with a single CBOE VIX row seeded into the shared ParadeDB,
/// the test drives the in-process MCP server via the official client SDK's
/// <see cref="HttpClientTransport"/> and asserts that <c>GetVixHistory</c> returns the
/// seeded date and OHLC values inside a text content block. The seeded value (14.95 close)
/// is deliberately uncommon so a "tool ran on an empty DB and silently returned the
/// no-data string" regression cannot match it.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class McpServerToolInvocationTests : IAsyncLifetime {
    private readonly ParadeDbFixture _paradeDb;
    private readonly CultureInfo _previousCulture;
    private McpServerFixture _serverFixture;

    public McpServerToolInvocationTests(ParadeDbFixture paradeDb) {
        _paradeDb = paradeDb;
        // GetVixHistory formats OHLC with :F2 which is culture-sensitive; the server reads
        // the request thread's CurrentCulture, not invariant — without pinning, a comma-
        // decimal host would render "14,95" and the assertion below would flake.
        _previousCulture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
    }

    public async Task InitializeAsync() {
        await _paradeDb.ResetAsync();

        // Seed one VIX row on the same DB the server reads through DI. Using the fixture's
        // CreateDbContext keeps EF Core registrations identical to production.
        await using (var ctx = _paradeDb.CreateDbContext()) {
            ctx.Set<CboeVixDaily>().Add(new CboeVixDaily {
                Date = new DateOnly(2026, 4, 1),
                Open = 14.20m, High = 15.30m, Low = 13.80m, Close = 14.95m,
            });
            await ctx.SaveChangesAsync();
        }

        _serverFixture = new McpServerFixture(_paradeDb);
        await _serverFixture.InitializeAsync();
    }

    public async Task DisposeAsync() {
        if (_serverFixture is not null) {
            await ((IAsyncLifetime)_serverFixture).DisposeAsync();
        }
        CultureInfo.CurrentCulture = _previousCulture;
    }

    [Fact]
    public async Task CallTool_GetVixHistory_OverHttpProtocol_ReturnsSeededRowInTextContentBlock() {
        var httpClient = _serverFixture.CreateClient();
        httpClient.BaseAddress = new Uri("http://localhost/");

        var transport = new HttpClientTransport(
            new HttpClientTransportOptions {
                Endpoint = new Uri("http://localhost/mcp"),
                TransportMode = HttpTransportMode.StreamableHttp,
            },
            httpClient);

        await using var client = await McpClient.CreateAsync(transport);

        // Date range deliberately spans the seeded row. Passing explicit strings exercises
        // JSON-RPC argument marshalling into the [Description]-decorated string parameters
        // — defaults would also work, but explicit args pin the binding path.
        var result = await client.CallToolAsync(
            toolName: "GetVixHistory",
            arguments: new Dictionary<string, object> {
                ["startDate"] = "2026-03-01",
                ["endDate"] = "2026-04-30",
            });

        result.IsError.Should().NotBe(true,
            "the MCP framework signals tool-level failures via IsError rather than throwing");

        var text = string.Concat(result.Content.OfType<TextContentBlock>().Select(b => b.Text));

        text.Should().Contain("CBOE Volatility Index (VIX)",
            "the tool method's success header should reach the client through the protocol");
        text.Should().Contain("2026-04-01",
            "the seeded date should be present in the rendered table");
        // F2 formatting respects the request thread's CurrentCulture — accept either decimal
        // separator so the assertion survives invariant (en-US, "14.95") and comma-decimal
        // (pt-PT, "14,95") hosts. The point of this assertion is that the seeded close
        // crossed the protocol boundary, not which locale formatted it.
        text.Should().MatchRegex(@"14[.,]95",
            "the F2-formatted close should round-trip through the JSON content block");
    }
}

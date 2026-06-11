using Equibles.IntegrationTests.Helpers;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace Equibles.IntegrationTests.Mcp.Server;

/// <summary>
/// A client that sends a required argument with the wrong JSON type — an array where
/// the tool declares a string — made a mistake in its request, not in our server: the
/// SDK's binder throws <see cref="System.Text.Json.JsonException"/> before the tool
/// body runs, and without translation that surfaces as an unhandled exception logged
/// at error level with an opaque message. The contract pinned here is that such a call
/// comes back as a structured tool error — <c>IsError</c> with a message naming the
/// tool and calling out invalid parameters — so the client can fix its request and the
/// server's error log stays meaningful.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class McpServerMismatchedArgumentTypeTests : IAsyncLifetime
{
    private readonly ParadeDbFixture _paradeDb;
    private McpServerFixture _serverFixture;

    public McpServerMismatchedArgumentTypeTests(ParadeDbFixture paradeDb)
    {
        _paradeDb = paradeDb;
    }

    public async Task InitializeAsync()
    {
        await _paradeDb.ResetAsync();
        _serverFixture = new McpServerFixture(_paradeDb);
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
    public async Task CallTool_GetLatestPricesWithArrayForStringTickers_ReturnsInvalidParamsError()
    {
        var httpClient = _serverFixture.CreateClient();
        httpClient.BaseAddress = new Uri("http://localhost/");

        var transport = new HttpClientTransport(
            new HttpClientTransportOptions
            {
                Endpoint = new Uri("http://localhost/mcp"),
                TransportMode = HttpTransportMode.StreamableHttp,
            },
            httpClient
        );

        await using var client = await McpClient.CreateAsync(transport);

        // GetLatestPrices declares 'tickers' as a comma-separated string — sending a
        // JSON array instead is the natural mistake for a client to make and trips
        // the binder's deserialization rather than its missing-argument check.
        var result = await client.CallToolAsync(
            toolName: "GetLatestPrices",
            arguments: new Dictionary<string, object> { ["tickers"] = new[] { "AAPL", "MSFT" } }
        );

        result
            .IsError.Should()
            .Be(true, "a call with a wrongly-typed argument cannot have succeeded");

        var text = string.Concat(result.Content.OfType<TextContentBlock>().Select(b => b.Text));
        text.Should()
            .Contain(
                "Invalid parameters",
                "the client should be told its arguments were the problem, not given an opaque server error"
            );
        text.Should()
            .Contain("GetLatestPrices", "the error should name the tool that rejected the call");
    }
}

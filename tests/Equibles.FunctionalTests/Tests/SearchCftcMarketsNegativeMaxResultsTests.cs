using Equibles.Cftc.Data.Models;
using Equibles.FunctionalTests.Fixtures;
using FluentAssertions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace Equibles.FunctionalTests.Tests;

/// <summary>
/// Adversarial end-to-end test for the SearchCftcMarkets MCP tool over the real client →
/// HTTP → ParadeDB pipeline. Contract: a search tool must degrade gracefully on malformed
/// input. A negative maxResults is malformed — a caller relies on the tool returning either
/// matching rows or a "no contracts found" message, never the generic internal-error
/// sentinel. The tool feeds maxResults straight into EF .Take(maxResults), so a negative
/// value becomes a negative SQL LIMIT that Postgres rejects, leaking the sentinel.
/// </summary>
[Trait("Category", "Functional")]
public class SearchCftcMarketsNegativeMaxResultsTests
    : IClassFixture<McpServerAppFixture>,
        IAsyncLifetime
{
    private readonly McpServerAppFixture _fixture;
    private McpClient _client;

    public SearchCftcMarketsNegativeMaxResultsTests(McpServerAppFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        var transport = new HttpClientTransport(
            new HttpClientTransportOptions
            {
                Endpoint = new Uri($"{_fixture.BaseUrl.TrimEnd('/')}/mcp"),
                TransportMode = HttpTransportMode.StreamableHttp,
            }
        );
        _client = await McpClient.CreateAsync(transport);
    }

    public async Task DisposeAsync()
    {
        if (_client is not null)
        {
            await _client.DisposeAsync();
        }
    }

    [Fact(Skip = "GH-2968 — SearchCftcMarkets negative maxResults leaks internal-error sentinel")]
    public async Task SearchCftcMarkets_NegativeMaxResults_DoesNotLeakInternalErrorSentinel()
    {
        await _fixture.ResetAndSeedAsync(async db =>
        {
            db.Set<CftcContract>()
                .Add(
                    new CftcContract
                    {
                        MarketCode = "088691",
                        MarketName = "GOLD",
                        Category = CftcContractCategory.Metals,
                    }
                );
            await db.SaveChangesAsync();
        });

        var tool = await GetTool("SearchCftcMarkets");
        var text = await CallToolForText(tool, new() { ["query"] = "gold", ["maxResults"] = -1 });

        // A negative limit must not crash into the generic catch-all; the caller should get
        // graceful degradation, not "An error occurred while executing ...".
        text.Should()
            .NotContain("An error occurred while executing SearchCftcMarkets. Please try again.");
    }

    private async Task<McpClientTool> GetTool(string name)
    {
        var tools = await _client.ListToolsAsync();
        return tools.First(t => t.Name == name);
    }

    private static async Task<string> CallToolForText(
        McpClientTool tool,
        Dictionary<string, object> arguments
    )
    {
        var result = await tool.CallAsync(arguments);
        var textBlock = result.Content.OfType<TextContentBlock>().FirstOrDefault();
        textBlock
            .Should()
            .NotBeNull(
                "every Equibles MCP tool returns its formatted output as a single TextContentBlock"
            );
        return textBlock.Text;
    }
}

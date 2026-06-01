using Equibles.Cftc.Data.Models;
using Equibles.FunctionalTests.Fixtures;
using FluentAssertions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace Equibles.FunctionalTests.Tests;

/// <summary>
/// Adversarial end-to-end test for the GetCftcPositioning MCP tool over the real client →
/// HTTP → ParadeDB pipeline. Contract: a query tool must degrade gracefully on malformed
/// input. A negative maxResults is malformed — a caller relies on the tool returning either
/// matching rows or a "no reports found" message, never the generic internal-error sentinel.
/// The tool feeds maxResults into EF .Take(Math.Max(0, maxResults)); this pins that the clamp
/// keeps a negative value from becoming a negative SQL LIMIT that Postgres rejects.
/// </summary>
[Trait("Category", "Functional")]
public class GetCftcPositioningNegativeMaxResultsTests
    : IClassFixture<McpServerAppFixture>,
        IAsyncLifetime
{
    private readonly McpServerAppFixture _fixture;
    private McpClient _client;

    public GetCftcPositioningNegativeMaxResultsTests(McpServerAppFixture fixture)
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

    [Fact]
    public async Task GetCftcPositioning_NegativeMaxResults_DoesNotLeakInternalErrorSentinel()
    {
        await _fixture.ResetAndSeedAsync(async db =>
        {
            var contract = new CftcContract
            {
                MarketCode = "088691",
                MarketName = "GOLD",
                Category = CftcContractCategory.Metals,
            };
            db.Set<CftcContract>().Add(contract);
            db.Set<CftcPositionReport>()
                .Add(
                    new CftcPositionReport
                    {
                        CftcContract = contract,
                        ReportDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-7)),
                        OpenInterest = 1000,
                        CommLong = 400,
                        CommShort = 300,
                        NonCommLong = 200,
                        NonCommShort = 100,
                    }
                );
            await db.SaveChangesAsync();
        });

        var tool = await GetTool("GetCftcPositioning");
        var text = await CallToolForText(
            tool,
            new() { ["marketCode"] = "088691", ["maxResults"] = -1 }
        );

        // A negative limit must not crash into the generic catch-all; the caller should get
        // graceful degradation, not "An error occurred while executing ...".
        text.Should()
            .NotContain("An error occurred while executing GetCftcPositioning. Please try again.");
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

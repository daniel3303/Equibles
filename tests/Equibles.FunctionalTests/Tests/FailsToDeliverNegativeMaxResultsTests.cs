using Equibles.CommonStocks.Data.Models;
using Equibles.FunctionalTests.Fixtures;
using Equibles.Sec.Data.Models;
using FluentAssertions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace Equibles.FunctionalTests.Tests;

/// <summary>
/// Adversarial end-to-end test for the GetFailsToDeliver MCP tool's <c>maxResults</c> parameter.
/// The contract ("Maximum number of records to return") makes a negative value nonsensical input
/// that must be handled gracefully — never surfaced to the caller as the tool's internal-failure
/// sentinel. The implementation feeds the raw client value straight into EF Core's
/// <c>.Take(maxResults)</c>, which emits a negative SQL <c>LIMIT</c> that Postgres rejects, so the
/// exception leaks out as the generic "An error occurred while executing" message. This mirrors
/// the same vein already pinned for GetStockPrices (GH-2931) and the other newest-first tools.
/// </summary>
[Trait("Category", "Functional")]
public class FailsToDeliverNegativeMaxResultsTests
    : IClassFixture<McpServerAppFixture>,
        IAsyncLifetime
{
    private readonly McpServerAppFixture _fixture;
    private McpClient _client;

    public FailsToDeliverNegativeMaxResultsTests(McpServerAppFixture fixture)
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
    public async Task GetFailsToDeliver_NegativeMaxResults_DoesNotSurfaceInternalError()
    {
        await _fixture.ResetAndSeedAsync(async db =>
        {
            var stock = new CommonStock
            {
                Ticker = "GME",
                Name = "GameStop Corp",
                Cik = "0001326380",
            };
            db.Set<CommonStock>().Add(stock);

            db.Set<FailToDeliver>()
                .Add(
                    new FailToDeliver
                    {
                        CommonStock = stock,
                        CommonStockId = stock.Id,
                        SettlementDate = new DateOnly(2026, 4, 1),
                        Quantity = 100_000,
                        Price = 25.50m,
                    }
                );
            await db.SaveChangesAsync();
        });

        var tools = await _client.ListToolsAsync();
        var tool = tools.First(t => t.Name == "GetFailsToDeliver");

        // A negative record cap is invalid input; the tool must degrade gracefully rather than
        // let the value reach the database as a negative SQL LIMIT. The generic
        // "An error occurred while executing" sentinel means an internal exception leaked out.
        var result = await tool.CallAsync(
            new Dictionary<string, object>
            {
                ["ticker"] = "GME",
                ["startDate"] = "2026-03-01",
                ["endDate"] = "2026-04-30",
                ["maxResults"] = -1,
            }
        );

        var text = result.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text;
        text.Should().NotBeNull();
        text.Should().NotContain("An error occurred while executing");
    }
}

using Equibles.CommonStocks.Data.Models;
using Equibles.FunctionalTests.Fixtures;
using FluentAssertions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace Equibles.FunctionalTests.Tests;

/// <summary>
/// Adversarial end-to-end test for the GetFundOperations MCP tool's <c>maxResults</c> parameter.
/// Once the ticker resolves, GetFundOperations feeds the raw client value straight into EF Core's
/// <c>.Take(maxResults)</c> with no clamping, so a negative cap becomes a negative SQL <c>LIMIT</c>
/// that PostgreSQL rejects; the exception is swallowed and the caller gets the generic
/// internal-failure sentinel. The parameter contract ("Maximum number of annual reports to return")
/// makes a negative value nonsensical input that must degrade gracefully — never surface as the
/// tool's internal error. Same defect class as GH-2931 (GetStockPrices) and GH-2980
/// (GetProposedSales), sibling MCP tools with the identical unclamped <c>.Take(maxResults)</c>.
/// </summary>
[Trait("Category", "Functional")]
public class GetFundOperationsNegativeMaxResultsTests
    : IClassFixture<McpServerAppFixture>,
        IAsyncLifetime
{
    private readonly McpServerAppFixture _fixture;
    private McpClient _client;

    public GetFundOperationsNegativeMaxResultsTests(McpServerAppFixture fixture)
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
    public async Task GetFundOperations_NegativeMaxResults_DoesNotSurfaceInternalError()
    {
        await _fixture.ResetAndSeedAsync(async db =>
        {
            db.Set<CommonStock>()
                .Add(
                    new CommonStock
                    {
                        Ticker = "SPY",
                        Name = "SPDR S&P 500 ETF Trust",
                        Cik = "0000884394",
                    }
                );
            await db.SaveChangesAsync();
        });

        var tools = await _client.ListToolsAsync();
        var tool = tools.First(t => t.Name == "GetFundOperations");

        // A negative report cap is invalid input for a "maximum number of annual reports" parameter;
        // the tool must degrade gracefully rather than let the value reach the database as a
        // negative LIMIT. The generic "An error occurred while executing" sentinel means an
        // internal exception leaked out — the behaviour this test forbids.
        var result = await tool.CallAsync(
            new Dictionary<string, object> { ["ticker"] = "SPY", ["maxResults"] = -1 }
        );

        var text = result.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text;
        text.Should().NotBeNull();
        text.Should().NotContain("An error occurred while executing");
    }
}

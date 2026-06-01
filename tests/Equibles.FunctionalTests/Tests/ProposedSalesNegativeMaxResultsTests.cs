using Equibles.CommonStocks.Data.Models;
using Equibles.FunctionalTests.Fixtures;
using Equibles.InsiderTrading.Data.Models;
using FluentAssertions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace Equibles.FunctionalTests.Tests;

/// <summary>
/// Adversarial end-to-end test for the GetProposedSales MCP tool's <c>maxResults</c> parameter.
/// Once a ticker resolves, GetProposedSales feeds the raw client value straight into EF Core's
/// <c>.Take(maxResults)</c>, so a negative cap becomes a negative SQL <c>LIMIT</c> that
/// PostgreSQL rejects; the exception is swallowed and the caller gets the generic
/// internal-failure sentinel. The parameter contract ("Maximum number of notices to return")
/// makes a negative value nonsensical input that must degrade gracefully — never surface as the
/// tool's internal error. Same defect class as GH-2931 (GetStockPrices) and GH-2978
/// (GetExemptOfferings), sibling MCP tools with the identical unclamped <c>.Take(maxResults)</c>.
/// </summary>
[Trait("Category", "Functional")]
public class ProposedSalesNegativeMaxResultsTests
    : IClassFixture<McpServerAppFixture>,
        IAsyncLifetime
{
    private readonly McpServerAppFixture _fixture;
    private McpClient _client;

    public ProposedSalesNegativeMaxResultsTests(McpServerAppFixture fixture)
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
    public async Task GetProposedSales_NegativeMaxResults_DoesNotSurfaceInternalError()
    {
        await _fixture.ResetAndSeedAsync(async db =>
        {
            var stock = new CommonStock
            {
                Ticker = "AAPL",
                Name = "Apple Inc",
                Cik = "0000320193",
            };
            db.Set<CommonStock>().Add(stock);
            await db.SaveChangesAsync();

            db.Set<Form144Filing>()
                .Add(
                    new Form144Filing
                    {
                        CommonStock = stock,
                        CommonStockId = stock.Id,
                        AccessionNumber = "0000320193-26-000001",
                        FilingDate = new DateOnly(2026, 4, 1),
                        SellerName = "Jane Affiliate",
                        RelationshipToIssuer = "Director",
                        SecurityClassTitle = "Common",
                        BrokerName = "Charles Schwab & Co., Inc.",
                        SharesToBeSold = 10_000,
                        AggregateMarketValue = 3_000_000m,
                        SharesOutstanding = 14_687_356_000,
                        ApproxSaleDate = new DateOnly(2026, 4, 7),
                        SecuritiesExchangeName = "NASDAQ",
                    }
                );
        });

        var tools = await _client.ListToolsAsync();
        var tool = tools.First(t => t.Name == "GetProposedSales");

        // A negative notice cap is invalid input for a "maximum number of notices" parameter;
        // the tool must degrade gracefully rather than let the value reach the database as a
        // negative LIMIT. The generic "An error occurred while executing" sentinel means an
        // internal exception leaked out — the behaviour this test forbids.
        var result = await tool.CallAsync(
            new Dictionary<string, object> { ["ticker"] = "AAPL", ["maxResults"] = -1 }
        );

        var text = result.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text;
        text.Should().NotBeNull();
        text.Should().NotContain("An error occurred while executing");
    }
}

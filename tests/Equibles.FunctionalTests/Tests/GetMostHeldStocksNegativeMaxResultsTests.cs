using Equibles.CommonStocks.Data.Models;
using Equibles.FunctionalTests.Fixtures;
using Equibles.Holdings.Data.Models;
using FluentAssertions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace Equibles.FunctionalTests.Tests;

/// <summary>
/// Adversarial end-to-end test for the GetMostHeldStocks MCP tool's <c>maxResults</c> parameter.
/// Once a report date and its prior quarter resolve, GetMostHeldStocks orders the cross-sectional
/// 13F ranking and feeds the raw client value straight into EF Core's <c>.Take(maxResults)</c>
/// (InstitutionalHoldingsTools.cs:718), so a negative cap becomes a negative SQL <c>LIMIT</c> that
/// PostgreSQL rejects; the resulting exception is swallowed and the caller gets the generic
/// internal-failure sentinel. The parameter contract ("Maximum number of stocks to return") makes
/// a negative value nonsensical input that must degrade gracefully — never surface as the tool's
/// internal error. Same defect class as GH-2931 (GetStockPrices) and GH-2959 (GetTopHolders);
/// GetMostHeldStocks is the Holdings module's market-wide sibling with the identical unclamped
/// <c>.Take(maxResults)</c>.
/// </summary>
[Trait("Category", "Functional")]
public class GetMostHeldStocksNegativeMaxResultsTests
    : IClassFixture<McpServerAppFixture>,
        IAsyncLifetime
{
    private readonly McpServerAppFixture _fixture;
    private McpClient _client;

    public GetMostHeldStocksNegativeMaxResultsTests(McpServerAppFixture fixture)
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
    public async Task GetMostHeldStocks_NegativeMaxResults_DoesNotSurfaceInternalError()
    {
        var stockId = Guid.NewGuid();
        var current = new DateOnly(2024, 12, 31);
        var prior = new DateOnly(2024, 9, 30);

        await _fixture.ResetAndSeedAsync(async db =>
        {
            db.Add(
                new CommonStock
                {
                    Id = stockId,
                    Ticker = "AAPL",
                    Name = "Apple Inc.",
                    Cik = "0000320193",
                }
            );

            var holder = new InstitutionalHolder
            {
                Cik = "0001067983",
                Name = "Berkshire Hathaway",
            };
            db.Add(holder);

            // Two report dates so ResolveMarketActivityDates finds a prior quarter to
            // compare against and execution reaches the unclamped .Take(maxResults).
            db.Add(
                new InstitutionalHolding
                {
                    CommonStockId = stockId,
                    InstitutionalHolderId = holder.Id,
                    ReportDate = current,
                    FilingDate = current.AddDays(45),
                    Value = 50_000_000L,
                    Shares = 1_000_000L,
                    ShareType = ShareType.Shares,
                    InvestmentDiscretion = InvestmentDiscretion.Sole,
                    AccessionNumber = "acc-current",
                }
            );
            db.Add(
                new InstitutionalHolding
                {
                    CommonStockId = stockId,
                    InstitutionalHolderId = holder.Id,
                    ReportDate = prior,
                    FilingDate = prior.AddDays(45),
                    Value = 40_000_000L,
                    Shares = 900_000L,
                    ShareType = ShareType.Shares,
                    InvestmentDiscretion = InvestmentDiscretion.Sole,
                    AccessionNumber = "acc-prior",
                }
            );
            await Task.CompletedTask;
        });

        var tools = await _client.ListToolsAsync();
        var tool = tools.First(t => t.Name == "GetMostHeldStocks");

        // A negative cap is invalid input for a "maximum number of stocks" parameter; the tool
        // must degrade gracefully rather than let the value reach the database as a negative
        // LIMIT. The generic "An error occurred while executing" sentinel means an internal
        // exception leaked out — the behaviour this test forbids.
        var result = await tool.CallAsync(new Dictionary<string, object> { ["maxResults"] = -1 });

        var text = result.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text;
        text.Should().NotBeNull();
        text.Should().NotContain("An error occurred while executing");
    }
}

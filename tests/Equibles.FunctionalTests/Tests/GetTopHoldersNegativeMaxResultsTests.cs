using Equibles.CommonStocks.Data.Models;
using Equibles.FunctionalTests.Fixtures;
using Equibles.Holdings.Data.Models;
using FluentAssertions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace Equibles.FunctionalTests.Tests;

/// <summary>
/// Adversarial end-to-end test for the GetTopHolders MCP tool's <c>maxResults</c> parameter.
/// Once a ticker resolves and a report date is found, GetTopHolders orders the holdings and
/// feeds the raw client value straight into EF Core's <c>.Take(maxResults)</c>, so a negative
/// cap becomes a negative SQL <c>LIMIT</c> that PostgreSQL rejects; the resulting exception is
/// swallowed and the caller gets the generic internal-failure sentinel. The parameter contract
/// ("Maximum number of holders to return") makes a negative value nonsensical input that must
/// degrade gracefully — never surface as the tool's internal error. Same defect class as
/// GH-2931 (GetStockPrices) and GH-2943 (GetShortVolume); GetTopHolders is the Holdings module's
/// unfiled sibling with the identical unclamped <c>.Take(maxResults)</c>.
/// </summary>
[Trait("Category", "Functional")]
public class GetTopHoldersNegativeMaxResultsTests
    : IClassFixture<McpServerAppFixture>,
        IAsyncLifetime
{
    private readonly McpServerAppFixture _fixture;
    private McpClient _client;

    public GetTopHoldersNegativeMaxResultsTests(McpServerAppFixture fixture)
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
    public async Task GetTopHolders_NegativeMaxResults_DoesNotSurfaceInternalError()
    {
        var stockId = Guid.NewGuid();
        var reportDate = new DateOnly(2025, 12, 31);

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

            db.Add(
                new InstitutionalHolding
                {
                    CommonStockId = stockId,
                    InstitutionalHolderId = holder.Id,
                    ReportDate = reportDate,
                    FilingDate = reportDate.AddDays(45),
                    Value = 50_000_000L,
                    Shares = 1_000_000L,
                    ShareType = ShareType.Shares,
                    InvestmentDiscretion = InvestmentDiscretion.Sole,
                }
            );
            await Task.CompletedTask;
        });

        var tools = await _client.ListToolsAsync();
        var tool = tools.First(t => t.Name == "GetTopHolders");

        // A negative holder cap is invalid input for a "maximum number of holders" parameter;
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

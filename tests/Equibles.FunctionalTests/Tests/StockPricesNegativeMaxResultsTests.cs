using Equibles.CommonStocks.Data.Models;
using Equibles.FunctionalTests.Fixtures;
using Equibles.Yahoo.Data.Models;
using FluentAssertions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace Equibles.FunctionalTests.Tests;

/// <summary>
/// Adversarial end-to-end test for the GetStockPrices MCP tool's <c>maxResults</c> parameter.
/// Its sibling indicator tools in the same class (Stochastic, ATR, Bollinger, OBV) all route
/// <c>maxResults</c> through <c>AppendNewestFirstRows</c>, whose <c>emitted &lt; maxResults</c>
/// guard degrades gracefully to zero rows on a non-positive value. GetStockPrices instead feeds
/// the raw client value into EF Core's <c>.Take(maxResults)</c>. The contract ("Maximum number
/// of records to return") makes a negative value nonsensical input that must be handled
/// gracefully — never surfaced to the caller as the tool's internal-failure sentinel.
/// </summary>
[Trait("Category", "Functional")]
public class StockPricesNegativeMaxResultsTests : IClassFixture<McpServerAppFixture>, IAsyncLifetime
{
    private readonly McpServerAppFixture _fixture;
    private McpClient _client;

    public StockPricesNegativeMaxResultsTests(McpServerAppFixture fixture)
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

    [Fact(
        Skip = "GH-2931 — GetStockPrices surfaces an internal error for a negative maxResults instead of degrading gracefully"
    )]
    public async Task GetStockPrices_NegativeMaxResults_DoesNotSurfaceInternalError()
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

            db.Set<DailyStockPrice>()
                .Add(
                    BuildPrice(stock, new DateOnly(2026, 4, 1), close: 175.50m, volume: 50_000_000)
                );
        });

        var tools = await _client.ListToolsAsync();
        var tool = tools.First(t => t.Name == "GetStockPrices");

        // A negative record cap is invalid input; the tool must degrade gracefully (like its
        // sibling indicator tools) rather than let the value reach the database. The generic
        // "An error occurred while executing" sentinel means an internal exception leaked out.
        var result = await tool.CallAsync(
            new Dictionary<string, object>
            {
                ["ticker"] = "AAPL",
                ["startDate"] = "2026-03-01",
                ["endDate"] = "2026-04-30",
                ["maxResults"] = -1,
            }
        );

        var text = result.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text;
        text.Should().NotBeNull();
        text.Should().NotContain("An error occurred while executing");
    }

    private static DailyStockPrice BuildPrice(
        CommonStock stock,
        DateOnly date,
        decimal close,
        long volume
    ) =>
        new()
        {
            CommonStock = stock,
            CommonStockId = stock.Id,
            Date = date,
            Open = close - 1m,
            High = close + 1m,
            Low = close - 2m,
            Close = close,
            AdjustedClose = close,
            Volume = volume,
        };
}

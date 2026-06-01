using Equibles.CommonStocks.Data.Models;
using Equibles.Finra.Data.Models;
using Equibles.FunctionalTests.Fixtures;
using FluentAssertions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace Equibles.FunctionalTests.Tests;

/// <summary>
/// Adversarial end-to-end test for the GetShortVolume MCP tool's <c>maxResults</c> parameter.
/// Once a ticker resolves, GetShortVolume feeds the raw client value straight into EF Core's
/// <c>.Take(maxResults)</c>, so a negative cap becomes a negative SQL <c>LIMIT</c> that
/// PostgreSQL rejects; the resulting exception is swallowed and the caller gets the generic
/// internal-failure sentinel. The parameter contract ("Maximum number of records to return")
/// makes a negative value nonsensical input that must degrade gracefully — never surface as the
/// tool's internal error. Same defect class as GH-2931 (GetStockPrices) and GH-2933
/// (GetVixHistory), sibling MCP tools with the identical unclamped <c>.Take(maxResults)</c>.
/// </summary>
[Trait("Category", "Functional")]
public class ShortVolumeNegativeMaxResultsTests : IClassFixture<McpServerAppFixture>, IAsyncLifetime
{
    private readonly McpServerAppFixture _fixture;
    private McpClient _client;

    public ShortVolumeNegativeMaxResultsTests(McpServerAppFixture fixture)
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
    public async Task GetShortVolume_NegativeMaxResults_DoesNotSurfaceInternalError()
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
            await db.SaveChangesAsync();

            db.Set<DailyShortVolume>()
                .Add(
                    new DailyShortVolume
                    {
                        CommonStock = stock,
                        CommonStockId = stock.Id,
                        Date = new DateOnly(2026, 4, 1),
                        ShortVolume = 1_200_000,
                        ShortExemptVolume = 50_000,
                        TotalVolume = 2_000_000,
                        Market = "Q",
                    }
                );
        });

        var tools = await _client.ListToolsAsync();
        var tool = tools.First(t => t.Name == "GetShortVolume");

        // A negative record cap is invalid input for a "maximum number of records" parameter;
        // the tool must degrade gracefully rather than let the value reach the database as a
        // negative LIMIT. The generic "An error occurred while executing" sentinel means an
        // internal exception leaked out — the behaviour this test forbids.
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

using Equibles.CommonStocks.Data.Models;
using Equibles.Congress.Data.Models;
using Equibles.FunctionalTests.Fixtures;
using FluentAssertions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace Equibles.FunctionalTests.Tests;

/// <summary>
/// Adversarial end-to-end test for the GetCongressionalTrades MCP tool's <c>maxResults</c>
/// parameter. Once the ticker resolves, the tool feeds the raw client value straight into EF
/// Core's <c>.Take(maxResults)</c> over an <see cref="IQueryable{T}"/>, so a negative cap
/// becomes a negative SQL <c>LIMIT</c> that PostgreSQL rejects (22023); the resulting exception
/// is swallowed by the tool runner and the caller gets the generic internal-failure sentinel.
/// The parameter contract ("Maximum number of trades to return") makes a negative value
/// nonsensical input that must degrade gracefully — never surface as the tool's internal error.
/// Same defect class as GH-2931 (GetStockPrices), GH-2933 (GetVixHistory), GH-2937
/// (GetPutCallRatios), GH-2939 (GetCftcPositioning), GH-2941 (GetEconomicIndicator), GH-2943
/// (GetShortVolume) and GH-2945 (GetFailsToDeliver) — sibling MCP tools with the identical
/// unclamped <c>.Take(maxResults)</c>.
/// </summary>
[Trait("Category", "Functional")]
public class CongressionalTradesNegativeMaxResultsTests
    : IClassFixture<McpServerAppFixture>,
        IAsyncLifetime
{
    private readonly McpServerAppFixture _fixture;
    private McpClient _client;

    public CongressionalTradesNegativeMaxResultsTests(McpServerAppFixture fixture)
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
        Skip = "GH-2947 — GetCongressionalTrades surfaces an internal error for a negative maxResults instead of degrading gracefully"
    )]
    public async Task GetCongressionalTrades_NegativeMaxResults_DoesNotSurfaceInternalError()
    {
        var stockId = Guid.NewGuid();
        var memberId = Guid.NewGuid();

        await _fixture.ResetAndSeedAsync(async db =>
        {
            db.Set<CommonStock>()
                .Add(
                    new CommonStock
                    {
                        Id = stockId,
                        Ticker = "NVDA",
                        Name = "NVIDIA Corp",
                        Cik = "0001045810",
                    }
                );
            db.Set<CongressMember>()
                .Add(
                    new CongressMember
                    {
                        Id = memberId,
                        Name = "Jane Senator",
                        Position = CongressPosition.Senator,
                    }
                );
            db.Set<CongressionalTrade>()
                .Add(
                    new CongressionalTrade
                    {
                        CommonStockId = stockId,
                        CongressMemberId = memberId,
                        TransactionDate = new DateOnly(2026, 4, 1),
                        FilingDate = new DateOnly(2026, 4, 3),
                        TransactionType = CongressTransactionType.Purchase,
                        OwnerType = "Self",
                        AssetName = "NVDA Common Stock",
                        AmountFrom = 1_000,
                        AmountTo = 15_000,
                    }
                );
            await Task.CompletedTask;
        });

        var tools = await _client.ListToolsAsync();
        var tool = tools.First(t => t.Name == "GetCongressionalTrades");

        // A negative record cap is invalid input for a "maximum number of trades" parameter;
        // the tool must degrade gracefully rather than let the value reach the database as a
        // negative LIMIT. The generic "An error occurred while executing" sentinel means an
        // internal exception leaked out — the behaviour this test forbids.
        var result = await tool.CallAsync(
            new Dictionary<string, object>
            {
                ["ticker"] = "NVDA",
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

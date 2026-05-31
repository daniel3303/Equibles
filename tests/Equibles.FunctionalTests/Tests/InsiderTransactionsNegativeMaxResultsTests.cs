using Equibles.CommonStocks.Data.Models;
using Equibles.FunctionalTests.Fixtures;
using Equibles.InsiderTrading.Data.Models;
using FluentAssertions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace Equibles.FunctionalTests.Tests;

/// <summary>
/// Adversarial end-to-end test for the GetInsiderTransactions MCP tool's <c>maxResults</c>
/// parameter. Once the ticker resolves, the tool feeds the raw client value straight into EF
/// Core's <c>.Take(maxResults)</c> over an <see cref="IQueryable{T}"/>, so a negative cap
/// becomes a negative SQL <c>LIMIT</c> that PostgreSQL rejects (22023); the resulting exception
/// is swallowed by the tool runner and the caller gets the generic internal-failure sentinel.
/// The parameter contract ("Maximum number of transactions to return") makes a negative value
/// nonsensical input that must degrade gracefully — never surface as the tool's internal error.
/// Same defect class as GH-2931 (GetStockPrices), GH-2933 (GetVixHistory), GH-2937
/// (GetPutCallRatios), GH-2939 (GetCftcPositioning), GH-2941 (GetEconomicIndicator), GH-2943
/// (GetShortVolume), GH-2945 (GetFailsToDeliver) and GH-2947 (GetCongressionalTrades) — sibling
/// MCP tools with the identical unclamped <c>.Take(maxResults)</c>.
/// </summary>
[Trait("Category", "Functional")]
public class InsiderTransactionsNegativeMaxResultsTests
    : IClassFixture<McpServerAppFixture>,
        IAsyncLifetime
{
    private readonly McpServerAppFixture _fixture;
    private McpClient _client;

    public InsiderTransactionsNegativeMaxResultsTests(McpServerAppFixture fixture)
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
        Skip = "GH-2949 — GetInsiderTransactions surfaces an internal error for a negative maxResults instead of degrading gracefully"
    )]
    public async Task GetInsiderTransactions_NegativeMaxResults_DoesNotSurfaceInternalError()
    {
        var stockId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();

        await _fixture.ResetAndSeedAsync(async db =>
        {
            db.Set<CommonStock>()
                .Add(
                    new CommonStock
                    {
                        Id = stockId,
                        Ticker = "AAPL",
                        Name = "Apple Inc",
                        Cik = "0000320193",
                    }
                );
            db.Set<InsiderOwner>()
                .Add(
                    new InsiderOwner
                    {
                        Id = ownerId,
                        OwnerCik = "0001214128",
                        Name = "Timothy D. Cook",
                        IsOfficer = true,
                        OfficerTitle = "Chief Executive Officer",
                    }
                );
            db.Set<InsiderTransaction>()
                .Add(
                    new InsiderTransaction
                    {
                        CommonStockId = stockId,
                        InsiderOwnerId = ownerId,
                        FilingDate = new DateOnly(2026, 4, 3),
                        TransactionDate = new DateOnly(2026, 4, 1),
                        TransactionCode = TransactionCode.Sale,
                        Shares = 100_000,
                        PricePerShare = 175.50m,
                        ReportedPricePerShare = 175.50m,
                        AcquiredDisposed = AcquiredDisposed.Disposed,
                        SharesOwnedAfter = 3_000_000,
                        OwnershipNature = OwnershipNature.Direct,
                        SecurityTitle = "Common Stock",
                        AccessionNumber = "0000320193-26-000042",
                        TransactionOrder = 0,
                    }
                );
            await Task.CompletedTask;
        });

        var tools = await _client.ListToolsAsync();
        var tool = tools.First(t => t.Name == "GetInsiderTransactions");

        // A negative record cap is invalid input for a "maximum number of transactions"
        // parameter; the tool must degrade gracefully rather than let the value reach the
        // database as a negative LIMIT. The generic "An error occurred while executing" sentinel
        // means an internal exception leaked out — the behaviour this test forbids.
        var result = await tool.CallAsync(
            new Dictionary<string, object> { ["ticker"] = "AAPL", ["maxResults"] = -1 }
        );

        var text = result.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text;
        text.Should().NotBeNull();
        text.Should().NotContain("An error occurred while executing");
    }
}

using Equibles.FunctionalTests.Fixtures;
using Equibles.Holdings.Data.Models;
using FluentAssertions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace Equibles.FunctionalTests.Tests;

/// <summary>
/// Adversarial end-to-end test for the SearchInstitutions MCP tool's <c>maxResults</c> parameter.
/// Once the name search matches a holder, SearchInstitutions feeds the raw client value straight
/// into EF Core's <c>.Take(maxResults)</c>, so a negative cap becomes a negative SQL <c>LIMIT</c>
/// that PostgreSQL rejects; the resulting exception is swallowed and the caller gets the generic
/// internal-failure sentinel. The parameter contract ("Maximum number of results to return")
/// makes a negative value nonsensical input that must degrade gracefully — never surface as the
/// tool's internal error. Same defect class as GH-2931 (GetStockPrices) and GH-2959
/// (GetTopHolders); SearchInstitutions is the Holdings module's unfiled bare-search sibling with
/// the identical unclamped <c>.Take(maxResults)</c>.
/// </summary>
[Trait("Category", "Functional")]
public class SearchInstitutionsNegativeMaxResultsTests
    : IClassFixture<McpServerAppFixture>,
        IAsyncLifetime
{
    private readonly McpServerAppFixture _fixture;
    private McpClient _client;

    public SearchInstitutionsNegativeMaxResultsTests(McpServerAppFixture fixture)
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
        Skip = "GH-2972 — SearchInstitutions surfaces an internal error for a negative maxResults instead of degrading gracefully"
    )]
    public async Task SearchInstitutions_NegativeMaxResults_DoesNotSurfaceInternalError()
    {
        await _fixture.ResetAndSeedAsync(async db =>
        {
            db.Add(
                new InstitutionalHolder
                {
                    Cik = "0001067983",
                    Name = "Berkshire Hathaway",
                    City = "Omaha",
                    StateOrCountry = "NE",
                }
            );
            await Task.CompletedTask;
        });

        var tools = await _client.ListToolsAsync();
        var tool = tools.First(t => t.Name == "SearchInstitutions");

        // A negative result cap is invalid input for a "maximum number of results" parameter;
        // the tool must degrade gracefully rather than let the value reach the database as a
        // negative LIMIT. The generic "An error occurred while executing" sentinel means an
        // internal exception leaked out — the behaviour this test forbids.
        var result = await tool.CallAsync(
            new Dictionary<string, object> { ["query"] = "Berkshire", ["maxResults"] = -1 }
        );

        var text = result.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text;
        text.Should().NotBeNull();
        text.Should().NotContain("An error occurred while executing");
    }
}

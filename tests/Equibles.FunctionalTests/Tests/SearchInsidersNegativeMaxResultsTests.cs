using Equibles.FunctionalTests.Fixtures;
using Equibles.InsiderTrading.Data.Models;
using FluentAssertions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace Equibles.FunctionalTests.Tests;

/// <summary>
/// Adversarial end-to-end test for the SearchInsiders MCP tool's <c>maxResults</c> parameter.
/// The tool feeds the raw client value straight into EF Core's <c>.Take(maxResults)</c> over the
/// deferred <see cref="IQueryable{T}"/> returned by <c>InsiderOwnerRepository.Search</c>, so a
/// negative cap becomes a negative SQL <c>LIMIT</c> that PostgreSQL rejects (22023); the resulting
/// exception is swallowed by the tool runner and the caller gets the generic internal-failure
/// sentinel. The parameter contract ("Maximum number of results") makes a negative value
/// nonsensical input that must degrade gracefully — never surface as the tool's internal error.
/// Same defect class as GH-2949 (GetInsiderTransactions) and GH-2957 (SearchCongressMembers) — the
/// bare-Search sibling with the identical unclamped <c>.Take(maxResults)</c>.
/// </summary>
[Trait("Category", "Functional")]
public class SearchInsidersNegativeMaxResultsTests
    : IClassFixture<McpServerAppFixture>,
        IAsyncLifetime
{
    private readonly McpServerAppFixture _fixture;
    private McpClient _client;

    public SearchInsidersNegativeMaxResultsTests(McpServerAppFixture fixture)
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
    public async Task SearchInsiders_NegativeMaxResults_DoesNotSurfaceInternalError()
    {
        await _fixture.ResetAndSeedAsync(async db =>
        {
            db.Set<InsiderOwner>()
                .Add(
                    new InsiderOwner
                    {
                        Id = Guid.NewGuid(),
                        OwnerCik = "0001214128",
                        Name = "Timothy D. Cook",
                        IsOfficer = true,
                        OfficerTitle = "Chief Executive Officer",
                    }
                );
            await Task.CompletedTask;
        });

        var tools = await _client.ListToolsAsync();
        var tool = tools.First(t => t.Name == "SearchInsiders");

        // A negative record cap is invalid input for a "maximum number of results" parameter;
        // the tool must degrade gracefully rather than let the value reach the database as a
        // negative LIMIT. The generic "An error occurred while executing" sentinel means an
        // internal exception leaked out — the behaviour this test forbids.
        var result = await tool.CallAsync(
            new Dictionary<string, object> { ["query"] = "Cook", ["maxResults"] = -1 }
        );

        var text = result.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text;
        text.Should().NotBeNull();
        text.Should().NotContain("An error occurred while executing");
    }
}

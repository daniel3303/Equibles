using Equibles.Fred.Data.Models;
using Equibles.FunctionalTests.Fixtures;
using FluentAssertions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace Equibles.FunctionalTests.Tests;

/// <summary>
/// Adversarial end-to-end test for the GetEconomicIndicator MCP tool's <c>maxResults</c> parameter.
/// GetEconomicIndicator feeds the raw client value straight into EF Core's <c>.Take(maxResults)</c>,
/// so a negative cap becomes a negative SQL <c>LIMIT</c> that PostgreSQL rejects; the resulting
/// exception is swallowed and the caller gets the generic internal-failure sentinel. The parameter
/// contract ("Maximum number of observations to return") makes a negative value nonsensical input
/// that must degrade gracefully — never surface as the tool's internal error. Same defect class and
/// identical <c>.Take(maxResults)</c> pattern as GetVixHistory (GH-2933), GetPutCallRatios (GH-2937),
/// and GetStockPrices (GH-2931).
/// </summary>
[Trait("Category", "Functional")]
public class EconomicIndicatorNegativeMaxResultsTests
    : IClassFixture<McpServerAppFixture>,
        IAsyncLifetime
{
    private readonly McpServerAppFixture _fixture;
    private McpClient _client;

    public EconomicIndicatorNegativeMaxResultsTests(McpServerAppFixture fixture)
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
    public async Task GetEconomicIndicator_NegativeMaxResults_DoesNotSurfaceInternalError()
    {
        await _fixture.ResetAndSeedAsync(async db =>
        {
            var series = new FredSeries
            {
                SeriesId = "FEDFUNDS",
                Title = "Federal Funds Effective Rate",
                Category = FredSeriesCategory.InterestRates,
                Frequency = "Monthly",
                Units = "Percent",
                SeasonalAdjustment = "Not Seasonally Adjusted",
            };
            db.Set<FredSeries>().Add(series);
            db.Set<FredObservation>()
                .Add(
                    new FredObservation
                    {
                        FredSeries = series,
                        Date = new DateOnly(2026, 4, 1),
                        Value = 5.33m,
                    }
                );
            await Task.CompletedTask;
        });

        var tools = await _client.ListToolsAsync();
        var tool = tools.First(t => t.Name == "GetEconomicIndicator");

        // A negative observation cap is invalid input for a "maximum number of records" parameter;
        // the tool must degrade gracefully rather than let the value reach the database as a
        // negative LIMIT. The generic "An error occurred while executing" sentinel means an
        // internal exception leaked out — the behaviour this test forbids.
        var result = await tool.CallAsync(
            new Dictionary<string, object>
            {
                ["seriesId"] = "FEDFUNDS",
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

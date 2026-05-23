using System.Net;
using Equibles.IntegrationTests.Helpers;

namespace Equibles.IntegrationTests.Web;

/// <summary>
/// Coverage: exercises the /holdings/trends route end-to-end with no seeded
/// data, verifying the view renders without error when both AUM and sector
/// allocation queries return empty results.
/// </summary>
[Collection(WebHostCollection.Name)]
public class HoldingsActivityControllerTrendsTests
{
    private readonly WebHostFixture _fixture;

    public HoldingsActivityControllerTrendsTests(WebHostFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Trends_NoHoldings_ReturnsOk()
    {
        await _fixture.ResetAndSeedAsync(_ => Task.CompletedTask);

        var response = await _fixture.Client.GetAsync("/holdings/trends");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}

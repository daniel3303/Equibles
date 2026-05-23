using System.Net;
using Equibles.IntegrationTests.Helpers;

namespace Equibles.IntegrationTests.Web;

/// <summary>
/// Coverage: exercises the /holdings/filings route with no seeded data and
/// a negative page parameter, verifying the page-clamp guard and empty-state
/// rendering both work without error.
/// </summary>
[Collection(WebHostCollection.Name)]
public class HoldingsActivityControllerLatestFilingsTests
{
    private readonly WebHostFixture _fixture;

    public HoldingsActivityControllerLatestFilingsTests(WebHostFixture fixture) =>
        _fixture = fixture;

    [Fact]
    public async Task LatestFilings_NegativePage_ReturnsOkWithClampedPage()
    {
        // page=-1 must be clamped to 1 and the view must render HTTP 200,
        // not throw or return a 500 from a negative Skip value.
        await _fixture.ResetAndSeedAsync(_ => Task.CompletedTask);

        var response = await _fixture.Client.GetAsync("/holdings/filings?page=-1");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}

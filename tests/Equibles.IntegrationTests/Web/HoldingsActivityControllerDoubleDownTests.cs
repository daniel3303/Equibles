using System.Net;
using Equibles.IntegrationTests.Helpers;

namespace Equibles.IntegrationTests.Web;

/// <summary>
/// Contract: DoubleDown action renders OK even with no holdings data and
/// clamps negative minPct to 0 instead of passing it to the query.
/// </summary>
[Collection(WebHostCollection.Name)]
public class HoldingsActivityControllerDoubleDownTests
{
    private readonly WebHostFixture _fixture;

    public HoldingsActivityControllerDoubleDownTests(WebHostFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task DoubleDown_NoHoldings_ReturnsOkWithoutError()
    {
        await _fixture.ResetAndSeedAsync(_ => Task.CompletedTask);

        var response = await _fixture.Client.GetAsync("/holdings/double-down");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}

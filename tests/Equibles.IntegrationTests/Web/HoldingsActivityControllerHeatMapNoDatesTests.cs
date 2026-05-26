using System.Net;
using Equibles.IntegrationTests.Helpers;

namespace Equibles.IntegrationTests.Web;

/// <summary>
/// Sibling to HoldingsActivityControllerDoubleDownTests (which pins the no-data
/// path on /holdings/double-down). The HeatMap action has the same cold-start
/// shape — `if (reportDates.Count < 2) return View(viewModel)` — but with its
/// own controller, route, view, and view model. A refactor that flipped the
/// guard's `< 2` to `<= 2` (or dropped it entirely) would crash on the
/// downstream `ResolveCombinedDateSelection` / `GetUniqueFilerIds` calls the
/// moment the universe has zero or one report dates. Pin the empty-data render.
/// </summary>
[Collection(WebHostCollection.Name)]
public class HoldingsActivityControllerHeatMapNoDatesTests
{
    private readonly WebHostFixture _fixture;

    public HoldingsActivityControllerHeatMapNoDatesTests(WebHostFixture fixture) =>
        _fixture = fixture;

    [Fact]
    public async Task HeatMap_NoHoldings_ReturnsOkWithoutError()
    {
        await _fixture.ResetAndSeedAsync(_ => Task.CompletedTask);

        var response = await _fixture.Client.GetAsync("/holdings/heatmap");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}

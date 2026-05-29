using System.Net;
using Equibles.IntegrationTests.Helpers;

namespace Equibles.IntegrationTests.Web;

/// <summary>
/// Contract: DoubleDown clamps a negative minPct to 0 rather than echoing it
/// or passing a negative threshold into the conviction query. The clamp branch
/// was uncovered (the existing no-args test never supplies a negative minPct),
/// so a regression that dropped the clamp would silently render "-5%+".
/// </summary>
[Collection(WebHostCollection.Name)]
public class HoldingsActivityControllerDoubleDownMinPctClampTests
{
    private readonly WebHostFixture _fixture;

    public HoldingsActivityControllerDoubleDownMinPctClampTests(WebHostFixture fixture) =>
        _fixture = fixture;

    [Fact]
    public async Task DoubleDown_NegativeMinPct_ClampsToZeroInRenderedForm()
    {
        await _fixture.ResetAndSeedAsync(_ => Task.CompletedTask);

        var response = await _fixture.Client.GetAsync("/holdings/double-down-report?minPct=-5");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        // The header copy always renders the threshold (F0); the filter form is
        // gated on >=2 quarters, so assert the always-rendered text instead.
        html.Should().Contain("0%+ quarter over quarter"); // -5 clamped to 0
        html.Should().NotContain("-5%+"); // never echoes the raw negative input
    }
}

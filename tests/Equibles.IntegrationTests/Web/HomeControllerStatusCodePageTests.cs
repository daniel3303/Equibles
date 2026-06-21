using System.Net;
using Equibles.IntegrationTests.Helpers;
using Xunit;

namespace Equibles.IntegrationTests.Web;

/// <summary>
/// Contract: a status-code response with no body — most importantly a 404 from
/// an unmatched route — must re-execute to the branded <c>HomeController.Error</c>
/// page rather than reach the user as an empty browser error. The global
/// <c>UseExceptionHandler</c> only covers thrown exceptions, so without
/// <c>UseStatusCodePagesWithReExecute</c> in the pipeline a bare 404 returns an
/// empty body. These tests pin that the re-execution is wired and preserves the
/// original status code.
/// </summary>
[Collection(WebHostCollection.Name)]
public class HomeControllerStatusCodePageTests
{
    private readonly WebHostFixture _fixture;

    public HomeControllerStatusCodePageTests(WebHostFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task UnmatchedRoute_RendersBrandedNotFoundPage_With404Status()
    {
        var response = await _fixture.Client.GetAsync("/this-route-does-not-exist");

        response
            .StatusCode.Should()
            .Be(
                HttpStatusCode.NotFound,
                "the re-executed error page must preserve the original 404 status"
            );

        var html = await response.Content.ReadAsStringAsync();
        html.Should()
            .Contain(
                "Page Not Found",
                "a 404 must reach the user as the branded error view, not an empty body"
            );
        html.Should()
            .Contain("Go Home", "the error view offers a recovery link back to the home page");
    }
}

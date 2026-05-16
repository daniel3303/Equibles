using Equibles.IntegrationTests.Helpers;
using Xunit;

namespace Equibles.IntegrationTests.Web;

/// <summary>
/// Contract: <c>HomeController.Error</c> backs the route
/// <c>/Home/Error/{statusCode?}</c> and the global exception handler. As a
/// user-facing endpoint it must emit a syntactically valid HTTP response —
/// status codes are constrained to >= 100 (RFC 9110 / ASP.NET Core). The
/// implementation sets <c>Response.StatusCode</c> verbatim from the route value,
/// so <c>/Home/Error/0</c> (a valid int) produces an out-of-range status. Only
/// the 404/500 arms are pinned elsewhere; the out-of-range boundary is not.
/// </summary>
[Collection(WebHostCollection.Name)]
public class HomeControllerErrorOutOfRangeStatusTests
{
    private readonly WebHostFixture _fixture;

    public HomeControllerErrorOutOfRangeStatusTests(WebHostFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Error_RouteStatusCodeIsZero_RespondsWithValidHttpStatus()
    {
        var response = await _fixture.Client.GetAsync("/Home/Error/0");

        ((int)response.StatusCode)
            .Should()
            .BeGreaterThanOrEqualTo(
                100,
                "a user-facing error endpoint must emit a valid HTTP status (>= 100), "
                    + "not echo an out-of-range route value straight onto Response.StatusCode"
            );
    }
}

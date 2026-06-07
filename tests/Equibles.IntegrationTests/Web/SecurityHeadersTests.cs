using System.Net;
using Equibles.IntegrationTests.Helpers;
using Xunit;

namespace Equibles.IntegrationTests.Web;

/// <summary>
/// Pins the baseline security response headers emitted by UseSecurityHeaders. The home
/// route needs no seed data and no auth, so it exercises the middleware end-to-end while
/// confirming the page still renders successfully with the headers in place.
/// </summary>
[Collection(WebHostCollection.Name)]
public class SecurityHeadersTests
{
    private readonly WebHostFixture _fixture;

    public SecurityHeadersTests(WebHostFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task GetIndex_EmitsBaselineSecurityHeaders()
    {
        var response = await _fixture.Client.GetAsync("/");

        response
            .StatusCode.Should()
            .Be(HttpStatusCode.OK, "headers must not break normal rendering");
        response
            .Headers.GetValues("X-Content-Type-Options")
            .Should()
            .ContainSingle()
            .Which.Should()
            .Be("nosniff");
        response
            .Headers.GetValues("X-Frame-Options")
            .Should()
            .ContainSingle()
            .Which.Should()
            .Be("DENY");
        response
            .Headers.GetValues("Referrer-Policy")
            .Should()
            .ContainSingle()
            .Which.Should()
            .Be("strict-origin-when-cross-origin");
        response
            .Headers.GetValues("Content-Security-Policy")
            .Should()
            .ContainSingle()
            .Which.Should()
            .Contain("frame-ancestors 'none'")
            .And.Contain("default-src 'self'");
    }
}

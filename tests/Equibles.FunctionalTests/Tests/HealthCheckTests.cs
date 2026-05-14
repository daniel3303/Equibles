using Equibles.FunctionalTests.Fixtures;
using FluentAssertions;
using Xunit;

namespace Equibles.FunctionalTests.Tests;

[Collection(FunctionalTestCollection.Name)]
[Trait("Category", "Functional")]
public class HealthCheckTests
{
    private readonly WebAppFixture _web;
    private readonly PlaywrightFixture _playwright;

    public HealthCheckTests(WebAppFixture web, PlaywrightFixture playwright)
    {
        _web = web;
        _playwright = playwright;
    }

    [Fact]
    public async Task Healthz_Get_ReturnsHealthyStatus()
    {
        // Smoke test: drives a real browser through the full Kestrel + EF Core + ParadeDB stack
        // to /healthz. Catches regressions that unit and integration tests can't:
        //   - missing AddHealthChecks() / MapHealthChecks() wiring
        //   - migration failures against the real schema
        //   - DI graph that compiles but throws at first request
        //   - data-protection key directory misconfiguration
        var page = await _playwright.NewPageAsync(_web.BaseUrl);

        var response = await page.GotoAsync("/healthz");

        response.Should().NotBeNull();
        response!.Status.Should().Be(200);
        var body = await page.ContentAsync();
        body.Should().Contain("Healthy");
    }
}

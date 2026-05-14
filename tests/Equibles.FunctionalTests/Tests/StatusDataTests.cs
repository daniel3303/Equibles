using System.Text.Json;
using Equibles.FunctionalTests.Fixtures;
using FluentAssertions;
using Microsoft.Playwright;
using Xunit;

namespace Equibles.FunctionalTests.Tests;

[Collection(FunctionalTestCollection.Name)]
[Trait("Category", "Functional")]
public class StatusDataTests
{
    private readonly WebAppFixture _web;
    private readonly PlaywrightFixture _playwright;

    public StatusDataTests(WebAppFixture web, PlaywrightFixture playwright)
    {
        _web = web;
        _playwright = playwright;
    }

    [Fact]
    public async Task Data_GetWithEmptyDatabase_ReturnsJsonPayloadWithAllDataCountsAndWorkerStatuses()
    {
        // /status/data is the dashboard's auto-refresh JSON endpoint. The action stitches together
        // DataCountService (11 distinct counts), the worker matrix, and error totals — a single
        // missing/renamed key here would silently break the live-refresh script on the Status
        // page. Anonymous DTOs in ASP.NET Core's Json() helper serialise to camelCase, so the
        // assertion pins the wire-level property names too.
        await _web.ResetAndSeedAsync();

        var page = await _playwright.NewPageAsync(_web.BaseUrl);
        var response = await page.GotoAsync("/status/data");

        response.Should().NotBeNull();
        response!.Status.Should().Be(200);
        response.Headers.GetValueOrDefault("content-type").Should().Contain("application/json");

        var body = await response.TextAsync();
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        // Anonymous-object property names round-trip through ASP.NET Core's camelCase policy,
        // but Dictionary<string, int> keys are written through verbatim — pin both contracts.
        root.GetProperty("databaseConnected")
            .GetBoolean()
            .Should()
            .BeTrue("the Testcontainers Postgres instance is reachable during the request");
        root.GetProperty("dataCounts")
            .GetProperty("StockCount")
            .GetInt32()
            .Should()
            .Be(0, "ResetAndSeedAsync truncates every user table before each test");
        root.GetProperty("workers")
            .GetArrayLength()
            .Should()
            .BeGreaterThan(
                0,
                "BuildWorkerStatuses returns a non-empty matrix even with no API keys configured"
            );
        root.GetProperty("totalErrorCount").GetInt32().Should().Be(0);
        root.GetProperty("unseenErrorCount").GetInt32().Should().Be(0);
        root.GetProperty("activeWorkerCount").ValueKind.Should().Be(JsonValueKind.Number);
        root.GetProperty("totalWorkerCount")
            .GetInt32()
            .Should()
            .Be(
                root.GetProperty("workers").GetArrayLength(),
                "TotalWorkerCount must mirror the rendered worker matrix"
            );
    }
}

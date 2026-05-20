using Equibles.FunctionalTests.Fixtures;
using Equibles.Messaging.Contracts.Activity;
using Equibles.Web.Services.Activity;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Xunit;

namespace Equibles.FunctionalTests.Tests;

[Collection(FunctionalTestCollection.Name)]
[Trait("Category", "Functional")]
public class StatusActivityTests
{
    private readonly WebAppFixture _web;
    private readonly PlaywrightFixture _playwright;

    public StatusActivityTests(WebAppFixture web, PlaywrightFixture playwright)
    {
        _web = web;
        _playwright = playwright;
    }

    [Fact]
    public async Task Activity_PublishedAfterOpen_AppearsInTheLiveList()
    {
        // Drive a real browser to /Status/Activity and confirm the page wires
        // EventSource → SSE → DOM render. After the page opens, publish a
        // ScraperActivity through the in-process broadcaster and assert the
        // browser shows it. Without a Playwright check, the SSE plumbing only
        // proves correct at the HTTP layer, not at the EventSource layer.
        var page = await _playwright.NewPageAsync(_web.BaseUrl);
        var response = await page.GotoAsync("/status/activity");

        response.Should().NotBeNull();
        response!.Status.Should().Be(200);

        await Assertions.Expect(page.Locator("h1")).ToHaveTextAsync("Live activity");

        // Wait until the SSE connection is established before publishing — the
        // server only forwards events that arrive AFTER a subscribe, and
        // EventSource opens asynchronously.
        await Assertions
            .Expect(page.Locator("[data-activity-status-text]"))
            .ToHaveTextAsync("Connected");

        // Publish a unique-per-test message so a re-used browser context
        // can't make the assertion pass on a stale row.
        var unique = $"e2e-activity-{Guid.NewGuid():N}";
        using var scope = _web.Services.CreateScope();
        var broadcaster = scope.ServiceProvider.GetRequiredService<ActivityFeedBroadcaster>();
        broadcaster.Publish(
            new ScraperActivity(
                Source: "SEC",
                Severity: ScraperActivitySeverity.Info,
                Message: unique,
                Timestamp: DateTimeOffset.UtcNow,
                CorrelationId: Guid.NewGuid().ToString()
            )
        );

        await Assertions.Expect(page.GetByText(unique)).ToBeVisibleAsync(new() { Timeout = 5000 });
    }
}

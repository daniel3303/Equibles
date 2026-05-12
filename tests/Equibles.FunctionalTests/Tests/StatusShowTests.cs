using Equibles.Errors.Data.Models;
using Equibles.FunctionalTests.Fixtures;
using FluentAssertions;
using Microsoft.Playwright;
using Xunit;

namespace Equibles.FunctionalTests.Tests;

[Collection(FunctionalTestCollection.Name)]
[Trait("Category", "Functional")]
public class StatusShowTests {
    private readonly WebAppFixture _web;
    private readonly PlaywrightFixture _playwright;

    public StatusShowTests(WebAppFixture web, PlaywrightFixture playwright) {
        _web = web;
        _playwright = playwright;
    }

    [Fact]
    public async Task Show_GetForSeededUnseenError_RendersErrorDetailsAndMarkAsSeenForm() {
        // /status/{id} loads the Error by Guid and renders Show.cshtml. With Seen=false the view
        // conditionally renders the 'Mark as Seen' form. This test seeds a single unseen error,
        // hits the route, and pins the wire-level + rendered shape:
        //   - 200 status (not the RedirectToAction(Index) that fires on missing id)
        //   - 'Error Details' h1
        //   - 'Mark as Seen' submit button (the Seen == false branch)
        // Together those distinguish the populated detail render from both the not-found redirect
        // and the seen-state branch.
        var errorId = Guid.NewGuid();
        await _web.ResetAndSeedAsync(async db => {
            db.Add(new Error {
                Id = errorId,
                Source = ErrorSource.Other,
                Context = "TestScraper",
                Message = "Synthetic test error for functional coverage",
                StackTrace = "at System.Test()",
                Seen = false,
            });
            await Task.CompletedTask;
        });

        var page = await _playwright.NewPageAsync(_web.BaseUrl);
        // Route resolves through BaseController's [Route("{controller}/{action}/{id?}")] +
        // StatusController.Show's [HttpGet("{id:guid}")], i.e. /status/show/{guid}.
        var response = await page.GotoAsync($"/status/show/{errorId}");

        response.Should().NotBeNull();
        response!.Status.Should().Be(200);

        await Assertions.Expect(page.Locator("h1")).ToHaveTextAsync("Error Details");
        await Assertions.Expect(page.Locator("button").Filter(new() { HasTextString = "Mark as Seen" }))
            .ToHaveCountAsync(1);
    }
}

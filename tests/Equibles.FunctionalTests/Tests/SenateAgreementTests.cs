using Equibles.Congress.HostedService.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Playwright;
using Xunit;

namespace Equibles.FunctionalTests.Tests;

// The Senate eFD session in its real Firefox, with every eFD request answered in the browser
// context, so nothing reaches the network.
[Trait("Category", "Functional")]
public class SenateAgreementTests
{
    private const string Origin = "https://efdsearch.senate.gov";

    // Contract: the agreement counts only once eFD redirects to the search page. Akamai can
    // answer the agreement POST with a 200 challenge page that re-submits it by script a
    // moment later; a search sent before that lands is refused for want of the agreement.
    [Fact]
    public async Task EnsureAuthenticated_AgreementBehindAChallengePage_SearchesOnlyOnceAccepted()
    {
        InstallFirefox();
        var site = new FakeEfd();
        await using var session = new PlaywrightSenateBrowserSession(
            NullLogger<PlaywrightSenateBrowserSession>.Instance,
            new OpenGate(),
            new RoutedOpener(site)
        );

        await session.EnsureAuthenticated(CancellationToken.None);
        var search = await session.Fetch(
            Origin + "/search/report/data/",
            new Dictionary<string, string> { ["start"] = "0", ["length"] = "5" },
            CancellationToken.None
        );

        site.AgreementPosts.Should().Be(2);
        search.Status.Should().Be(200);
    }

    private static void InstallFirefox()
    {
        // Idempotent; the session launches Firefox itself.
        Microsoft.Playwright.Program.Main(["install", "firefox"]).Should().Be(0);
    }

    private sealed class FakeEfd
    {
        private const string Home = """
            <html><head><title>eFD: Home</title></head><body>
            <form method="post" action="/search/home/">
            <input type="checkbox" id="agree_statement" name="prohibition_agreement" value="1"
                onchange="this.form.submit()">
            </form></body></html>
            """;

        // Akamai Bot Manager's interstitial, shortened: it re-posts the form once its script ran.
        private const string Challenge = """
            <html><head><title>Loading https://efdsearch.senate.gov/search/home/</title></head><body>
            <form id="retry" method="post" action="/search/home/">
            <input type="hidden" name="prohibition_agreement" value="1"></form>
            <script>setTimeout(() => document.getElementById('retry').submit(), 500);</script>
            </body></html>
            """;

        private bool _agreed;

        public int AgreementPosts { get; private set; }

        public Task Answer(IRoute route)
        {
            var request = route.Request;
            var path = new Uri(request.Url).AbsolutePath;
            if (path == "/search/home/" && request.Method == "POST")
            {
                if (++AgreementPosts == 1)
                    return Html(route, Challenge);
                _agreed = true;
                return route.FulfillAsync(
                    new RouteFulfillOptions
                    {
                        Status = 302,
                        Headers = new Dictionary<string, string>
                        {
                            ["Location"] = Origin + "/search/",
                        },
                    }
                );
            }
            if (path == "/search/home/")
                return Html(route, Home);
            if (path == "/search/")
                return Html(
                    route,
                    "<html><head><title>eFD: Search</title></head><body>Search</body></html>"
                );
            if (path == "/search/report/data/")
                return _agreed
                    ? route.FulfillAsync(
                        new RouteFulfillOptions
                        {
                            Status = 200,
                            ContentType = "application/json",
                            Body = """{"recordsTotal": 0, "data": []}""",
                        }
                    )
                    : route.FulfillAsync(
                        new RouteFulfillOptions
                        {
                            Status = 403,
                            Body = "Prohibition agreement required to search report data.",
                        }
                    );
            return route.FulfillAsync(new RouteFulfillOptions { Status = 404 });
        }

        private static Task Html(IRoute route, string body) =>
            route.FulfillAsync(
                new RouteFulfillOptions
                {
                    Status = 200,
                    ContentType = "text/html",
                    Body = body,
                }
            );
    }

    private sealed class RoutedOpener(FakeEfd site) : ISenateBrowserContextOpener
    {
        public async Task<SenateBrowserContext> Open(
            IBrowser browser,
            string homeUrl,
            Func<IBrowserContext, Task<IResponse>> loadHome
        )
        {
            var context = await browser.NewContextAsync();
            // The agreement wait has failed only on CI runners; the trace there shows what the page did.
            await context.Tracing.StartAsync(
                new TracingStartOptions
                {
                    Screenshots = true,
                    Snapshots = true,
                    Sources = false,
                }
            );
            await context.RouteAsync(Origin + "/**", site.Answer);
            var home = await loadHome(context);
            return new SenateBrowserContext(
                home,
                async ValueTask () =>
                {
                    var traces = Path.Combine(AppContext.BaseDirectory, "playwright-traces");
                    Directory.CreateDirectory(traces);
                    await context.Tracing.StopAsync(
                        new TracingStopOptions
                        {
                            Path = Path.Combine(traces, "senate-agreement.zip"),
                        }
                    );
                    await context.CloseAsync();
                }
            );
        }
    }

    private sealed class OpenGate() : SenateAccessGate(null, null)
    {
        public override Task ThrowIfHeld(CancellationToken ct) => Task.CompletedTask;
    }
}

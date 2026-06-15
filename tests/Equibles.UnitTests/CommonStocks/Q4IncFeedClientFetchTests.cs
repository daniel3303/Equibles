using System.Net;
using System.Text;
using Equibles.CommonStocks.HostedService.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Equibles.UnitTests.CommonStocks;

/// <summary>
/// Pins Q4IncFeedClient.Fetch's contract: when a stealth sidecar is configured the feed
/// is pulled through the cleared stealth session (no plain HTTP); when none is configured
/// it falls back to plain HTTP and returns null for a non-XML response so callers skip the
/// feed.
/// </summary>
public class Q4IncFeedClientFetchTests
{
    private const string FeedXml =
        "<?xml version=\"1.0\"?><rss version=\"2.0\"><channel><title>News</title></channel></rss>";

    [Fact]
    public async Task Fetch_Sidecar_PullsFeedThroughStealth_WithoutPlainHttp()
    {
        // With a sidecar configured the feed is pulled through the stealth session; the
        // plain HttpClient must never be touched (its handler throws if it is).
        var stealth = Substitute.For<IStealthBrowserClient>();
        stealth.IsEnabled.Returns(true);
        stealth.FetchRaw(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(FeedXml);

        var client = new Q4IncFeedClient(
            new HttpClient(new ThrowingHandler()),
            stealth,
            NullLogger<Q4IncFeedClient>.Instance
        );

        var result = await client.Fetch(
            "https://ir.example.com/",
            Q4IncFeedClient.NewsFeedPath,
            CancellationToken.None
        );

        result.Should().Be(FeedXml);
        await stealth
            .Received(1)
            .FetchRaw("https://ir.example.com/rss/pressrelease.aspx", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Fetch_Sidecar_FetchRawThrows_DegradesToNull()
    {
        // A sidecar fetch can fail with an exception (e.g. a navigation timeout) instead of the
        // contractual null. Fetch must degrade that to null, not let it bubble out — otherwise one
        // throwing feed aborts the content scrape and starves the rest of the cohort's news/events.
        var stealth = Substitute.For<IStealthBrowserClient>();
        stealth.IsEnabled.Returns(true);
        stealth
            .FetchRaw(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<string>(new TimeoutException("navigation timeout")));

        var client = new Q4IncFeedClient(
            new HttpClient(new ThrowingHandler()),
            stealth,
            NullLogger<Q4IncFeedClient>.Instance
        );

        string result = null;
        var act = async () =>
            result = await client.Fetch(
                "https://ir.example.com/",
                Q4IncFeedClient.NewsFeedPath,
                CancellationToken.None
            );

        await act.Should().NotThrowAsync();
        result.Should().BeNull();
    }

    [Fact]
    public async Task Fetch_NoSidecar_XmlFeed_ReturnsBodyViaPlainHttp()
    {
        // Standalone build with no sidecar: an XML feed is returned over plain HTTP.
        var client = new Q4IncFeedClient(
            new HttpClient(new StubHandler(HttpStatusCode.OK, "application/rss+xml", FeedXml)),
            DisabledStealth(),
            NullLogger<Q4IncFeedClient>.Instance
        );

        var result = await client.Fetch(
            "https://ir.example.com/",
            Q4IncFeedClient.NewsFeedPath,
            CancellationToken.None
        );

        result.Should().Be(FeedXml);
    }

    [Fact]
    public async Task Fetch_NoSidecar_HtmlBody_ReturnsNull()
    {
        // No sidecar: an HTML body (an error page or a bot wall) is not a feed, so a
        // status-code-only check must not hand it to the RSS parser as data.
        var client = new Q4IncFeedClient(
            new HttpClient(
                new StubHandler(
                    HttpStatusCode.OK,
                    "text/html",
                    "<html><body>Page not found</body></html>"
                )
            ),
            DisabledStealth(),
            NullLogger<Q4IncFeedClient>.Instance
        );

        var result = await client.Fetch(
            "https://ir.example.com/",
            Q4IncFeedClient.NewsFeedPath,
            CancellationToken.None
        );

        result.Should().BeNull("an HTML body is not a feed, regardless of the 200 status");
    }

    private static IStealthBrowserClient DisabledStealth()
    {
        var stealth = Substitute.For<IStealthBrowserClient>();
        stealth.IsEnabled.Returns(false);
        return stealth;
    }

    // Fails the test if plain HTTP is used — proves the sidecar path never falls through
    // to the HttpClient when a sidecar is configured.
    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) =>
            throw new InvalidOperationException(
                "plain HTTP must not be used when a sidecar is configured"
            );
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _mediaType;
        private readonly string _body;

        public StubHandler(HttpStatusCode status, string mediaType, string body)
        {
            _status = status;
            _mediaType = mediaType;
            _body = body;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) =>
            Task.FromResult(
                new HttpResponseMessage(_status)
                {
                    Content = new StringContent(_body, Encoding.UTF8, _mediaType),
                }
            );
    }
}

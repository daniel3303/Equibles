using System.Net;
using System.Text;
using Equibles.CommonStocks.HostedService.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Equibles.UnitTests.CommonStocks;

/// <summary>
/// Pins Q4IncFeedClient.Fetch's contract: it returns null for a non-XML response so
/// callers can skip the feed, and — when the stealth path is enabled — pulls the
/// feed through the cleared stealth session when a bot wall answers with a challenge
/// stub instead of XML.
/// </summary>
public class Q4IncFeedClientFetchTests
{
    private const string IncapsulaStub =
        "<html><head><script src=\"/_Incapsula_Resource?SWJIYLWA=719d\"></script></head>"
        + "<body>Request unsuccessful.</body></html>";

    private const string FeedXml =
        "<?xml version=\"1.0\"?><rss version=\"2.0\"><channel><title>News</title></channel></rss>";

    [Fact]
    public async Task Fetch_HtmlBodyWithOkStatus_ReturnsNull()
    {
        // An ordinary HTML error page (not a bot challenge) is not a feed: a
        // status-code-only check would hand it to the RSS parser as if it were data.
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

    [Fact]
    public async Task Fetch_BotChallenge_StealthEnabled_PullsFeedThroughStealth()
    {
        // The plain fetch is answered by a challenge stub; the stealth session clears
        // the wall and returns the raw feed XML the parser needs.
        var stealth = Substitute.For<IStealthBrowserClient>();
        stealth.IsEnabled.Returns(true);
        stealth.FetchRaw(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(FeedXml);

        var client = new Q4IncFeedClient(
            new HttpClient(new StubHandler(HttpStatusCode.OK, "text/html", IncapsulaStub)),
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
    public async Task Fetch_BotChallenge_StealthDisabled_ReturnsNull()
    {
        // With the sidecar off, a challenge is a plain miss and the stealth path is
        // never touched.
        var stealth = DisabledStealth();

        var client = new Q4IncFeedClient(
            new HttpClient(new StubHandler(HttpStatusCode.OK, "text/html", IncapsulaStub)),
            stealth,
            NullLogger<Q4IncFeedClient>.Instance
        );

        var result = await client.Fetch(
            "https://ir.example.com/",
            Q4IncFeedClient.NewsFeedPath,
            CancellationToken.None
        );

        result.Should().BeNull();
        await stealth.DidNotReceive().FetchRaw(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    private static IStealthBrowserClient DisabledStealth()
    {
        var stealth = Substitute.For<IStealthBrowserClient>();
        stealth.IsEnabled.Returns(false);
        return stealth;
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

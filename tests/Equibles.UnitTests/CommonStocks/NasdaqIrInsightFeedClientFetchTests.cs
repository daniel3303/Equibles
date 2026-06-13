using System.Net;
using System.Text;
using Equibles.CommonStocks.HostedService.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Equibles.UnitTests.CommonStocks;

/// <summary>
/// Pins NasdaqIrInsightFeedClient.Fetch's bot-challenge fallback: a host that
/// answers the feed with a challenge stub instead of XML is pulled through the
/// cleared stealth session when it is enabled, and is a plain miss when it is not.
/// </summary>
public class NasdaqIrInsightFeedClientFetchTests
{
    private const string AkamaiBlock =
        "<html><head><title>Access Denied</title></head>"
        + "<body>You don't have permission to access this resource.</body></html>";

    private const string FeedXml =
        "<?xml version=\"1.0\"?><rss version=\"2.0\"><channel><title>Events</title></channel></rss>";

    [Fact]
    public async Task Fetch_BotChallenge_StealthEnabled_PullsFeedThroughStealth()
    {
        var stealth = Substitute.For<IStealthBrowserClient>();
        stealth.IsEnabled.Returns(true);
        stealth.FetchRaw(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(FeedXml);

        // Akamai serves its block as HTML with a 403; the body still carries the
        // vendor signature, so the stealth session clears it and returns the feed.
        var client = new NasdaqIrInsightFeedClient(
            new HttpClient(new StubHandler(HttpStatusCode.Forbidden, "text/html", AkamaiBlock)),
            stealth,
            NullLogger<NasdaqIrInsightFeedClient>.Instance
        );

        var result = await client.Fetch(
            "https://investors.example.com/",
            NasdaqIrInsightFeedClient.EventsFeedPath,
            CancellationToken.None
        );

        result.Should().Be(FeedXml);
        await stealth
            .Received(1)
            .FetchRaw("https://investors.example.com/rss/events.xml", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Fetch_BotChallenge_StealthDisabled_ReturnsNull()
    {
        var stealth = Substitute.For<IStealthBrowserClient>();
        stealth.IsEnabled.Returns(false);

        var client = new NasdaqIrInsightFeedClient(
            new HttpClient(new StubHandler(HttpStatusCode.Forbidden, "text/html", AkamaiBlock)),
            stealth,
            NullLogger<NasdaqIrInsightFeedClient>.Instance
        );

        var result = await client.Fetch(
            "https://investors.example.com/",
            NasdaqIrInsightFeedClient.EventsFeedPath,
            CancellationToken.None
        );

        result.Should().BeNull();
        await stealth.DidNotReceive().FetchRaw(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Fetch_XmlFeed_ReturnsBodyWithoutStealth()
    {
        var stealth = Substitute.For<IStealthBrowserClient>();
        stealth.IsEnabled.Returns(true);

        var client = new NasdaqIrInsightFeedClient(
            new HttpClient(new StubHandler(HttpStatusCode.OK, "application/rss+xml", FeedXml)),
            stealth,
            NullLogger<NasdaqIrInsightFeedClient>.Instance
        );

        var result = await client.Fetch(
            "https://investors.example.com/",
            NasdaqIrInsightFeedClient.EventsFeedPath,
            CancellationToken.None
        );

        result.Should().Be(FeedXml);
        await stealth.DidNotReceive().FetchRaw(Arg.Any<string>(), Arg.Any<CancellationToken>());
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

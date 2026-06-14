using System.Net;
using System.Text;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.HostedService.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Equibles.UnitTests.CommonStocks;

public class InvestorRelationsProbeClientTests
{
    private const string RenderedIrPage =
        "<html><head><title>Investor Relations - Emergent</title></head>"
        + "<body><h1>Quarterly results</h1></body></html>";

    private const string IncapsulaStub =
        "<html><head><script src=\"/_Incapsula_Resource?SWJIYLWA=719d\"></script>"
        + "</head><body>Request unsuccessful.</body></html>";

    [Fact]
    public async Task Discover_Sidecar_RendersCandidate_WithoutAnyPlainHttp()
    {
        // With a sidecar configured, every candidate is rendered through the stealth
        // browser (most IR hosts are bot-protected). The plain HttpClient must never be
        // touched — its handler throws if it is.
        var stealth = Substitute.For<IStealthBrowserClient>();
        stealth.IsEnabled.Returns(true);
        stealth.FetchHtml(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(RenderedIrPage);

        var client = new InvestorRelationsProbeClient(
            new HttpClient(new ThrowingHandler()),
            stealth,
            NullLogger<InvestorRelationsProbeClient>.Instance
        );

        var result = await client.Discover(
            "emergentbiosolutions.com",
            ["investors"],
            [],
            CancellationToken.None
        );

        result.Should().NotBeNull();
        result!.Url.Should().Be("https://emergentbiosolutions.com/investors");
        result.Platform.Should().Be(IrPlatformType.Custom);
        await stealth.Received(1).FetchHtml(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Discover_NoSidecar_DirectIrPage_ResolvesViaPlainHttp()
    {
        // Standalone build with no sidecar: a reachable IR page resolves over plain HTTP
        // and the stealth path is never touched.
        var stealth = DisabledStealth();

        var client = new InvestorRelationsProbeClient(
            new HttpClient(
                new ConstantHandler(
                    HttpStatusCode.OK,
                    "text/html",
                    "<html><head><title>Investor Relations - Acme</title></head><body></body></html>"
                )
            ),
            stealth,
            NullLogger<InvestorRelationsProbeClient>.Instance
        );

        var result = await client.Discover("acme.com", ["investors"], [], CancellationToken.None);

        result.Should().NotBeNull();
        result!.Url.Should().Be("https://acme.com/investors");
        await stealth.DidNotReceive().FetchHtml(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Discover_NoSidecar_BotWall_IsMiss()
    {
        // No sidecar: a bot-wall challenge stub is not a valid IR page, so it is a miss
        // and the stealth path is never touched.
        var stealth = DisabledStealth();

        var client = new InvestorRelationsProbeClient(
            new HttpClient(new ConstantHandler(HttpStatusCode.OK, "text/html", IncapsulaStub)),
            stealth,
            NullLogger<InvestorRelationsProbeClient>.Instance
        );

        var result = await client.Discover(
            "emergentbiosolutions.com",
            ["investors"],
            [],
            CancellationToken.None
        );

        result.Should().BeNull();
        await stealth.DidNotReceive().FetchHtml(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Discover_NoSidecar_RedirectAboveColumnCeiling_FallsBackToProbedCandidate()
    {
        // Plain path (no sidecar): the returned URL must stay within
        // CommonStock.InvestorRelationsUrl's 256-char ceiling. When a probe lands (after
        // redirects) on a longer URL, the short probed candidate is returned instead.
        var overLongFinalUrl = "https://acme.com/" + new string('a', 300);
        var client = new InvestorRelationsProbeClient(
            new HttpClient(new LongFinalUrlHandler(overLongFinalUrl)),
            DisabledStealth(),
            NullLogger<InvestorRelationsProbeClient>.Instance
        );

        var result = await client.Discover("https://acme.com", ["ir"], [], CancellationToken.None);

        result!.Url.Should().Be("https://acme.com/ir");
        result.Url.Length.Should().BeLessThanOrEqualTo(256);
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

    // Returns a valid IR page (title alone satisfies the validator) but reports a
    // post-redirect RequestUri far longer than the 256-char column ceiling.
    private sealed class LongFinalUrlHandler : HttpMessageHandler
    {
        private readonly string _finalUrl;

        public LongFinalUrlHandler(string finalUrl) => _finalUrl = finalUrl;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "<html><head><title>Investor Relations</title></head><body>Welcome</body></html>",
                    Encoding.UTF8,
                    "text/html"
                ),
                // Explicitly set so HttpClient doesn't overwrite it: simulates the
                // request having landed on an over-long URL after redirects.
                RequestMessage = new HttpRequestMessage(HttpMethod.Get, _finalUrl),
            };
            return Task.FromResult(response);
        }
    }

    // Returns a fixed status, content-type, and body for every request.
    private sealed class ConstantHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _mediaType;
        private readonly string _body;

        public ConstantHandler(HttpStatusCode status, string mediaType, string body)
        {
            _status = status;
            _mediaType = mediaType;
            _body = body;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            return Task.FromResult(
                new HttpResponseMessage(_status)
                {
                    Content = new StringContent(_body, Encoding.UTF8, _mediaType),
                }
            );
        }
    }
}

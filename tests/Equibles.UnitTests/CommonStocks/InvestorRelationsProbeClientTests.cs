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
    public async Task Discover_Sidecar_PlainHttpResolves_SidecarNotTouched()
    {
        // Plain HTTP is tried first even when a sidecar is configured: a reachable IR page resolves
        // over plain HTTP and the contended stealth sidecar is never touched.
        var stealth = Substitute.For<IStealthBrowserClient>();
        stealth.IsEnabled.Returns(true);
        stealth.FetchHtml(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(RenderedIrPage);

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
    public async Task Discover_Sidecar_PlainHttpBotWalled_FallsBackToSidecarRender()
    {
        // When plain HTTP only gets a bot-wall challenge (which fails validation), discovery
        // escalates to the stealth render — and only then — which resolves the real IR page.
        var stealth = Substitute.For<IStealthBrowserClient>();
        stealth.IsEnabled.Returns(true);
        stealth.FetchHtml(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(RenderedIrPage);

        var plain = new ConstantHandler(HttpStatusCode.OK, "text/html", IncapsulaStub);
        var client = new InvestorRelationsProbeClient(
            new HttpClient(plain),
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
        plain.Calls.Should().BeGreaterThan(0); // plain HTTP was tried before the sidecar
        await stealth.Received().FetchHtml(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Discover_Sidecar_PlainHttpMissesAndSidecarThrows_DegradesToMiss()
    {
        // Plain HTTP misses (bot wall), then the sidecar render fails with an exception (e.g. a
        // navigation timeout) instead of the contractual null. The probe must degrade that to a
        // miss — if it bubbles out of Discover, the caller skips the definitive-miss back-off, so
        // the stock is never stamped, re-occupies every batch, and starves the rest of the universe.
        var stealth = Substitute.For<IStealthBrowserClient>();
        stealth.IsEnabled.Returns(true);
        stealth
            .FetchHtml(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<string>(new TimeoutException("navigation timeout")));

        var client = new InvestorRelationsProbeClient(
            new HttpClient(new ConstantHandler(HttpStatusCode.OK, "text/html", IncapsulaStub)),
            stealth,
            NullLogger<InvestorRelationsProbeClient>.Instance
        );

        IrDiscoveryResult result = null;
        var act = async () =>
            result = await client.Discover("acme.com", ["investors"], [], CancellationToken.None);

        await act.Should().NotThrowAsync();
        result.Should().BeNull();
        await stealth.Received().FetchHtml(Arg.Any<string>(), Arg.Any<CancellationToken>());
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

    [Theory]
    [InlineData("https://acme.com")]
    [InlineData("acme.com")] // EDGAR data often omits the scheme; the crawl must still run.
    public async Task Discover_NoSidecar_GuessedPathsMiss_FollowsHomepageIrLink(string website)
    {
        // The guessed paths/subdomains don't validate, but the homepage links to the real IR
        // page at a non-guessed location (/en-us/investors) — the homepage crawl follows it.
        const string homepage =
            "<html><head><title>Acme Corp</title></head><body>"
            + "<a href=\"/en-us/investors\">Investors</a></body></html>";
        const string irPage =
            "<html><head><title>Investor Relations - Acme</title></head>"
            + "<body><h1>Quarterly results</h1></body></html>";
        const string notIr =
            "<html><head><title>Page not found</title></head><body>404</body></html>";

        var handler = new RoutingHandler(uri =>
        {
            var key = uri.Host + uri.AbsolutePath;
            return key == "acme.com/" ? homepage
                : key == "acme.com/en-us/investors" ? irPage
                : notIr;
        });
        var client = new InvestorRelationsProbeClient(
            new HttpClient(handler),
            DisabledStealth(),
            NullLogger<InvestorRelationsProbeClient>.Instance
        );

        var result = await client.Discover(
            website,
            ["investors", "ir"],
            ["ir"],
            CancellationToken.None
        );

        result.Should().NotBeNull();
        result!.Url.Should().Be("https://acme.com/en-us/investors");
    }

    private static IStealthBrowserClient DisabledStealth()
    {
        var stealth = Substitute.For<IStealthBrowserClient>();
        stealth.IsEnabled.Returns(false);
        return stealth;
    }

    // Serves a body chosen per request URL, echoing the request as the final URL.
    private sealed class RoutingHandler : HttpMessageHandler
    {
        private readonly Func<Uri, string> _bodyFor;

        public RoutingHandler(Func<Uri, string> bodyFor) => _bodyFor = bodyFor;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) =>
            Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        _bodyFor(request.RequestUri!),
                        Encoding.UTF8,
                        "text/html"
                    ),
                    RequestMessage = request,
                }
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

    // Returns a fixed status, content-type, and body for every request, counting how many
    // requests it served (so a test can prove the plain-HTTP path was exercised).
    private sealed class ConstantHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _mediaType;
        private readonly string _body;
        private int _calls;

        public ConstantHandler(HttpStatusCode status, string mediaType, string body)
        {
            _status = status;
            _mediaType = mediaType;
            _body = body;
        }

        public int Calls => _calls;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Interlocked.Increment(ref _calls);
            return Task.FromResult(
                new HttpResponseMessage(_status)
                {
                    Content = new StringContent(_body, Encoding.UTF8, _mediaType),
                }
            );
        }
    }
}

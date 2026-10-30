using System.Net;
using Equibles.CommonStocks.HostedService.Services;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.UnitTests.CommonStocks;

/// <summary>
/// Contract: a website disclosed without a scheme ("www.acme.co.in") names no protocol, and
/// many small companies serve plain HTTP only. <c>WebsiteProbeClient.Validate</c> assumes
/// https first, then tries plain HTTP when every https probe misses, and persists the
/// http URL when the answer comes from the candidate's own site; a candidate that stated
/// https never falls back.
/// </summary>
public class WebsiteProbeClientPlainHttpFallbackTests
{
    [Theory]
    [InlineData("www.acme.co.in", "https://www.acme.co.in", "http://www.acme.co.in")]
    [InlineData(
        " acme.co.in/investors ",
        "https://acme.co.in/investors",
        "http://acme.co.in/investors"
    )]
    [InlineData("https://www.acme.co.in", "https://www.acme.co.in", null)]
    [InlineData("http://www.acme.co.in", "http://www.acme.co.in", null)]
    public void PlainHttp_OnlyForAnAssumedScheme(
        string candidate,
        string normalized,
        string expected
    )
    {
        WebsiteProbeClient.PlainHttp(candidate, normalized).Should().Be(expected);
    }

    [Fact]
    public async Task Validate_SchemelessCandidateServesOnlyPlainHttp_ReturnsHttpUrl()
    {
        var handler = new ScriptedHandler(uri =>
            uri.Scheme == Uri.UriSchemeHttp ? HttpStatusCode.OK : null
        );

        var result = await Build(handler).Validate("www.acme.co.in", CancellationToken.None);

        result.Should().Be("http://www.acme.co.in");
        handler
            .Requested.Select(u => u.ToString())
            .Should()
            .Equal("https://www.acme.co.in/", "https://acme.co.in/", "http://www.acme.co.in/");
    }

    // A redirect within the company's own registrable domain is its site; one to another site
    // (a webmail login, a parked or sold domain) is not.
    [Theory]
    [InlineData("https://acme.co.in/home", "http://www.acme.co.in")]
    [InlineData("http://investors.acme.co.in/", "http://www.acme.co.in")]
    [InlineData("https://www.webmailhost.com/cgi-bin/login.cgi", null)]
    [InlineData("https://another.co.in/", null)]
    public async Task Validate_PlainHttpIsJudgedByWhereItLands(string landed, string expected)
    {
        var handler = new ScriptedHandler(
            uri => uri.Scheme == Uri.UriSchemeHttp ? HttpStatusCode.OK : null,
            _ => new Uri(landed)
        );

        var result = await Build(handler).Validate("www.acme.co.in", CancellationToken.None);

        result.Should().Be(expected);
    }

    [Fact]
    public async Task Validate_StatedHttpsFails_NeverProbesPlainHttp()
    {
        var handler = new ScriptedHandler(uri =>
            uri.Scheme == Uri.UriSchemeHttp ? HttpStatusCode.OK : null
        );

        var result = await Build(handler)
            .Validate("https://www.acme.co.in", CancellationToken.None);

        result.Should().BeNull();
        handler.Requested.Should().OnlyContain(u => u.Scheme == Uri.UriSchemeHttps);
    }

    // A sidecar render cannot say where it landed, so with one configured the https probes
    // still render through it and the plain-HTTP fallback is a plain request.
    [Theory]
    [InlineData("http://acme.co.in/", "http://acme.co.in")]
    [InlineData("https://www.webmailhost.com/", null)]
    public async Task Validate_Sidecar_PlainHttpFallbackIsAPlainRequest(
        string landed,
        string expected
    )
    {
        var stealth = Substitute.For<IStealthBrowserClient>();
        stealth.IsEnabled.Returns(true);
        stealth.FetchHtml(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((string)null);
        var handler = new ScriptedHandler(_ => HttpStatusCode.OK, _ => new Uri(landed));

        var result = await Build(handler, stealth).Validate("acme.co.in", CancellationToken.None);

        result.Should().Be(expected);
        handler.Requested.Select(u => u.ToString()).Should().Equal("http://acme.co.in/");
        await stealth.DidNotReceive().FetchHtml("http://acme.co.in", Arg.Any<CancellationToken>());
    }

    private static WebsiteProbeClient Build(
        HttpMessageHandler handler,
        IStealthBrowserClient stealth = null
    ) =>
        new(
            new HttpClient(handler),
            stealth ?? DisabledStealth(),
            Substitute.For<ILogger<WebsiteProbeClient>>()
        );

    private static IStealthBrowserClient DisabledStealth()
    {
        var stealth = Substitute.For<IStealthBrowserClient>();
        stealth.IsEnabled.Returns(false);
        return stealth;
    }

    // Returns the scripted status for each probed URL, as answered from the scripted landing
    // address (the URL itself unless redirected); a null status throws, as a TLS or connection
    // failure does for HttpClient.
    private sealed class ScriptedHandler : HttpMessageHandler
    {
        private readonly Func<Uri, HttpStatusCode?> _statusFor;
        private readonly Func<Uri, Uri> _landedAt;

        public ScriptedHandler(Func<Uri, HttpStatusCode?> statusFor, Func<Uri, Uri> landedAt = null)
        {
            _statusFor = statusFor;
            _landedAt = landedAt ?? (uri => uri);
        }

        public List<Uri> Requested { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Requested.Add(request.RequestUri!);
            var status = _statusFor(request.RequestUri!);
            if (status == null)
                throw new HttpRequestException("The SSL connection could not be established");

            return Task.FromResult(
                new HttpResponseMessage(status.Value)
                {
                    RequestMessage = new HttpRequestMessage(
                        request.Method,
                        _landedAt(request.RequestUri!)
                    ),
                }
            );
        }
    }
}

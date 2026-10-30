using System.Net;
using Equibles.CommonStocks.HostedService.Services;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.UnitTests.CommonStocks;

/// <summary>
/// Contract: a website disclosed without a scheme ("www.acme.co.in") names no protocol, and
/// many small companies serve plain HTTP only. <c>WebsiteProbeClient.Validate</c> assumes
/// https first, then tries plain HTTP when every https probe misses, and persists the
/// http URL; a candidate that stated https never falls back.
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

    [Fact]
    public async Task Validate_Sidecar_RendersOnlyPlainHttp_ReturnsHttpUrl()
    {
        var stealth = Substitute.For<IStealthBrowserClient>();
        stealth.IsEnabled.Returns(true);
        stealth
            .FetchHtml(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call =>
                ((string)call[0]).StartsWith("http://", StringComparison.Ordinal)
                    ? "<html><body>Acme</body></html>"
                    : null
            );

        var result = await Build(new ScriptedHandler(_ => HttpStatusCode.OK), stealth)
            .Validate("acme.co.in", CancellationToken.None);

        result.Should().Be("http://acme.co.in");
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

    // Returns the scripted status for each probed URL; a null status throws, as a TLS or
    // connection failure does for HttpClient.
    private sealed class ScriptedHandler : HttpMessageHandler
    {
        private readonly Func<Uri, HttpStatusCode?> _statusFor;

        public ScriptedHandler(Func<Uri, HttpStatusCode?> statusFor) => _statusFor = statusFor;

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

            return Task.FromResult(new HttpResponseMessage(status.Value));
        }
    }
}

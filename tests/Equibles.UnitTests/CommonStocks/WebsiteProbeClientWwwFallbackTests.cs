using System.Net;
using Equibles.CommonStocks.HostedService.Services;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.UnitTests.CommonStocks;

/// <summary>
/// Contract: an investor-relations host disclosed with a leading "www."
/// (e.g. "www.investor.sonoco.com") usually publishes no "www" CNAME even though
/// the bare host serves the site. <c>WebsiteProbeClient.Validate</c> must retry
/// the bare host when the "www." variant fails, and persist the resolvable bare
/// host — otherwise a company with a reachable IR site is left with an empty
/// <c>CommonStock.Website</c> (and no downstream IR discovery). The "www." variant
/// still wins when it resolves, so hosts that do carry a "www" record are
/// untouched.
/// </summary>
public class WebsiteProbeClientWwwFallbackTests
{
    [Theory]
    [InlineData("https://www.investor.sonoco.com", "https://investor.sonoco.com")]
    [InlineData("https://www.acme.com", "https://acme.com")]
    [InlineData("https://www.acme.com/ir", "https://acme.com/ir")]
    [InlineData("http://www.ir.trinitycap.com", "http://ir.trinitycap.com")]
    public void WithoutWww_StripsLeadingWwwAndKeepsSchemeAndPath(string url, string expected)
    {
        WebsiteProbeClient.WithoutWww(url).Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("https://acme.com")] // no leading www.
    [InlineData("https://investor.acme.com")] // subdomain, but not www.
    [InlineData("https://www.com")] // stripping would leave a TLD-less host
    public void WithoutWww_ReturnsNull_WhenNoStrippableWwwHost(string url)
    {
        WebsiteProbeClient.WithoutWww(url).Should().BeNull();
    }

    [Fact]
    public async Task Validate_WwwSubdomainFailsButBareResolves_ReturnsBareHost()
    {
        var handler = new ScriptedHandler(uri =>
            uri.Host == "investor.sonoco.com" ? HttpStatusCode.OK : null
        );
        var sut = Build(handler);

        var result = await sut.Validate("www.investor.sonoco.com", CancellationToken.None);

        result.Should().Be("https://investor.sonoco.com");
        handler
            .Requested.Select(u => u.ToString())
            .Should()
            .ContainInOrder("https://www.investor.sonoco.com/", "https://investor.sonoco.com/");
    }

    [Fact]
    public async Task Validate_WwwHostResolves_KeepsWwwHostWithoutFallbackProbe()
    {
        var handler = new ScriptedHandler(_ => HttpStatusCode.OK);
        var sut = Build(handler);

        var result = await sut.Validate("www.investor.agilent.com", CancellationToken.None);

        result.Should().Be("https://www.investor.agilent.com");
        handler
            .Requested.Should()
            .ContainSingle()
            .Which.Host.Should()
            .Be("www.investor.agilent.com");
    }

    [Fact]
    public async Task Validate_BothVariantsFail_ReturnsNull()
    {
        var handler = new ScriptedHandler(_ => null);
        var sut = Build(handler);

        var result = await sut.Validate("www.investor.dead.com", CancellationToken.None);

        result.Should().BeNull();
    }

    private static WebsiteProbeClient Build(HttpMessageHandler handler) =>
        new(new HttpClient(handler), Substitute.For<ILogger<WebsiteProbeClient>>());

    // Returns the scripted status for each probed URL; a null status throws, mimicking
    // the DNS failure (NXDOMAIN) a missing "www" CNAME produces for HttpClient.
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
                throw new HttpRequestException("Name or service not known");

            return Task.FromResult(new HttpResponseMessage(status.Value));
        }
    }
}

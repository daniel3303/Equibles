using Equibles.CommonStocks.HostedService.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Equibles.UnitTests.CommonStocks;

public class WebsiteProbeClientTests
{
    [Fact]
    public async Task Validate_Sidecar_FetchThrows_DegradesToMiss()
    {
        // A sidecar render can fail with an exception (e.g. a navigation timeout) instead of the
        // contractual null. The reachability probe must degrade that to a miss, not let it bubble
        // out of the discovery cycle — otherwise the cycle's definitive-miss back-off never runs,
        // the stock is never stamped, and it re-occupies every batch, starving the rest of the
        // website-discovery universe (which in turn feeds IR-URL discovery).
        var stealth = Substitute.For<IStealthBrowserClient>();
        stealth.IsEnabled.Returns(true);
        stealth
            .FetchHtml(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<string>(new TimeoutException("navigation timeout")));

        var client = new WebsiteProbeClient(
            new HttpClient(new ThrowingHandler()),
            stealth,
            NullLogger<WebsiteProbeClient>.Instance
        );

        string result = null;
        var act = async () => result = await client.Validate("acme.com", CancellationToken.None);

        await act.Should().NotThrowAsync();
        result.Should().BeNull();
        await stealth.Received().FetchHtml(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("https://acme.com")]
    [InlineData("https://acme.com/")]
    [InlineData("not a url")]
    public void Alternatives_ABareStatedAddress_HasNone(string stored) =>
        WebsiteProbeClient.Alternatives(stored).Should().BeEmpty();

    [Fact]
    public void Alternatives_ARetiredPath_TriesItsSiteRootFirst() =>
        WebsiteProbeClient
            .Alternatives("https://www.acme.com/investors")
            .Should()
            .Equal("https://www.acme.com", "https://acme.com");

    [Fact]
    public void Alternatives_NoStatedScheme_LeavesPlainHttpToValidate() =>
        WebsiteProbeClient.Alternatives("acme.co.in").Should().BeEmpty();

    [Theory]
    [InlineData(StealthFetchStatus.PageUnavailable, WebsiteLiveness.Dead)]
    [InlineData(StealthFetchStatus.HostRefused, WebsiteLiveness.Unknown)]
    [InlineData(StealthFetchStatus.SidecarUnavailable, WebsiteLiveness.Unknown)]
    [InlineData(StealthFetchStatus.Rendered, WebsiteLiveness.Alive)]
    public async Task CheckLiveness_ASiteFailingAPlainRequest_FollowsTheSidecar(
        StealthFetchStatus status,
        WebsiteLiveness expected
    )
    {
        var stealth = Substitute.For<IStealthBrowserClient>();
        stealth.IsEnabled.Returns(true);
        stealth
            .TryFetchHtml(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new StealthFetchResult(status, null));
        var client = new WebsiteProbeClient(
            new HttpClient(new RefusingHandler()),
            stealth,
            NullLogger<WebsiteProbeClient>.Instance
        );

        (await client.CheckLiveness("https://acme.com", CancellationToken.None))
            .Should()
            .Be(expected);
    }

    private sealed class RefusingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.Forbidden));
    }

    // Fails the test if plain HTTP is used — proves the sidecar path never falls through to the
    // HttpClient when a sidecar is configured.
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
}

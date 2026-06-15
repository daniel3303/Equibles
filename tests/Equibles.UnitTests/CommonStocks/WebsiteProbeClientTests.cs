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

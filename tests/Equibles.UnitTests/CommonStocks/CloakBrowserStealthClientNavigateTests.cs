using System.Threading.Tasks;
using Equibles.CommonStocks.HostedService.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Playwright;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Equibles.UnitTests.CommonStocks;

/// <summary>
/// Contract: the stealth navigation waits for the network to fall idle, but a host that
/// streams background telemetry (e.g. the FDA advisory-committee calendar) never lets the
/// network go idle, so the idle wait times out even though the document is fully rendered.
/// That timeout must be non-fatal — the loaded DOM is kept — while a genuine navigation
/// failure still propagates so the fetch degrades to a miss rather than returning a
/// half-broken page.
/// </summary>
public class CloakBrowserStealthClientNavigateTests
{
    [Fact]
    public async Task NavigateWaitingForNetworkIdle_SwallowsIdleTimeout_SoTheLoadedDomIsKept()
    {
        var page = Substitute.For<IPage>();
        page.GotoAsync(Arg.Any<string>(), Arg.Any<PageGotoOptions>())
            .ThrowsAsync(new PlaywrightException("Timeout 45000ms exceeded."));

        var navigate = async () =>
            await CloakBrowserStealthClient.NavigateWaitingForNetworkIdle(
                page,
                "https://www.fda.gov/advisory-committees/advisory-committee-calendar",
                45000,
                NullLogger.Instance
            );

        await navigate.Should().NotThrowAsync();
    }

    [Fact]
    public async Task NavigateWaitingForNetworkIdle_Rethrows_OnGenuineNavigationFailure()
    {
        var page = Substitute.For<IPage>();
        page.GotoAsync(Arg.Any<string>(), Arg.Any<PageGotoOptions>())
            .ThrowsAsync(new PlaywrightException("net::ERR_CONNECTION_REFUSED"));

        var navigate = async () =>
            await CloakBrowserStealthClient.NavigateWaitingForNetworkIdle(
                page,
                "https://unreachable.example",
                45000,
                NullLogger.Instance
            );

        await navigate.Should().ThrowAsync<PlaywrightException>();
    }
}

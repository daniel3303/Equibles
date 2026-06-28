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
/// Contract: the stealth navigation waits only for DOMContentLoaded, then a brief settle for any
/// client-rendered / post-challenge content — not full network-idle, which most IR/company pages
/// (streaming background telemetry) never reach, burning the whole render budget. A settle-wait
/// timeout must be non-fatal — the loaded DOM is kept — while a genuine navigation failure on the
/// initial load still propagates so the fetch degrades to a miss rather than returning a broken page.
/// </summary>
public class CloakBrowserStealthClientNavigateTests
{
    [Fact]
    public async Task Navigate_WaitsForDomContentLoaded_NotNetworkIdle()
    {
        var page = Substitute.For<IPage>();

        await CloakBrowserStealthClient.NavigateWaitingForNetworkIdle(
            page,
            "https://www.example.com",
            45000,
            NullLogger.Instance
        );

        await page.Received()
            .GotoAsync(
                "https://www.example.com",
                Arg.Is<PageGotoOptions>(o => o.WaitUntil == WaitUntilState.DOMContentLoaded)
            );
    }

    [Fact]
    public async Task Navigate_SwallowsSettleTimeout_SoTheLoadedDomIsKept()
    {
        // The DOM loads, but the page streams background telemetry so the settle's network-idle wait
        // times out. That must be non-fatal — the already-loaded DOM is kept.
        var page = Substitute.For<IPage>();
        page.WaitForLoadStateAsync(Arg.Any<LoadState>(), Arg.Any<PageWaitForLoadStateOptions>())
            .ThrowsAsync(new PlaywrightException("Timeout 8000ms exceeded."));

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
    public async Task Navigate_SwallowsSystemTimeoutExceptionOnSettle_SoTheLoadedDomIsKept()
    {
        // Playwright .NET surfaces a wait timeout as System.TimeoutException — NOT a
        // PlaywrightException. The settle timeout must be non-fatal regardless of the concrete type.
        var page = Substitute.For<IPage>();
        page.WaitForLoadStateAsync(Arg.Any<LoadState>(), Arg.Any<PageWaitForLoadStateOptions>())
            .ThrowsAsync(new System.TimeoutException("Timeout 8000ms exceeded."));

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
    public async Task Navigate_Rethrows_OnGenuineNavigationFailure()
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

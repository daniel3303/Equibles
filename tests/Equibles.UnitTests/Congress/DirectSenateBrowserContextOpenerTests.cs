using Equibles.Congress.HostedService.Services;
using Microsoft.Playwright;
using NSubstitute;

namespace Equibles.UnitTests.Congress;

public class DirectSenateBrowserContextOpenerTests
{
    private const string HomeUrl = "https://efdsearch.senate.gov/search/home/";

    // Contract: the opened context carries the home page's answer and stays open
    // until the session disposes it.
    [Fact]
    public async Task Open_HomeLoaded_KeepsTheContextOpenUntilDisposed()
    {
        var context = Substitute.For<IBrowserContext>();
        var home = Substitute.For<IResponse>();

        var opened = await new DirectSenateBrowserContextOpener().Open(
            Browser(context),
            HomeUrl,
            loaded => Task.FromResult(loaded == context ? home : null)
        );

        opened.Home.Should().BeSameAs(home);
        await context.DidNotReceive().CloseAsync(Arg.Any<BrowserContextCloseOptions>());
        await opened.DisposeAsync();
        await context.Received(1).CloseAsync(Arg.Any<BrowserContextCloseOptions>());
    }

    // Contract: a home page that fails to load closes its context and surfaces the
    // failure, so the session never keeps a half-opened browser.
    [Fact]
    public async Task Open_LoadThrows_ClosesTheContextAndRethrows()
    {
        var context = Substitute.For<IBrowserContext>();

        var open = () =>
            new DirectSenateBrowserContextOpener().Open(
                Browser(context),
                HomeUrl,
                _ => throw new PlaywrightException("NS_ERROR_NET_RESET")
            );

        await open.Should().ThrowAsync<PlaywrightException>();
        await context.Received(1).CloseAsync(Arg.Any<BrowserContextCloseOptions>());
    }

    private static IBrowser Browser(IBrowserContext context)
    {
        var browser = Substitute.For<IBrowser>();
        browser.NewContextAsync(Arg.Any<BrowserNewContextOptions>()).Returns(context);
        return browser;
    }
}

using Equibles.Congress.HostedService.Services;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Equibles.UnitTests.Congress;

/// <summary>
/// <see cref="PlaywrightSenateBrowserSession.DisposeAsync"/> was 0% — the
/// browser-driven methods need a real Firefox and aren't exercised. Disposing a
/// never-authenticated session (page/browser/playwright all null) still must
/// run the full teardown path: suppress finalize, skip the null page/browser
/// closes, null-dispose Playwright, and dispose the init lock — without
/// throwing.
/// </summary>
public class PlaywrightSenateBrowserSessionDisposeTests
{
    [Fact]
    public async Task DisposeAsync_NeverAuthenticated_CompletesTeardownWithoutThrowing()
    {
        var session = new PlaywrightSenateBrowserSession(
            Substitute.For<ILogger<PlaywrightSenateBrowserSession>>()
        );

        var dispose = async () => await session.DisposeAsync();

        await dispose.Should().NotThrowAsync("the null-guarded teardown path must be safe");
    }
}

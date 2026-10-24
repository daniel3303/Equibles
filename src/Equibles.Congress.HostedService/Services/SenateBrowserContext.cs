using Microsoft.Playwright;

namespace Equibles.Congress.HostedService.Services;

/// <summary>
/// A browser context opened for a Senate eFD session, with the home page's answer
/// in it. Disposing it closes the context and whatever its opener attached.
/// </summary>
public sealed class SenateBrowserContext : IAsyncDisposable
{
    private readonly Func<ValueTask> _close;

    public SenateBrowserContext(IResponse home, Func<ValueTask> close)
    {
        Home = home;
        _close = close;
    }

    /// <summary>The home page's answer; null when its navigation returned none.</summary>
    public IResponse Home { get; }

    public ValueTask DisposeAsync() => _close();
}

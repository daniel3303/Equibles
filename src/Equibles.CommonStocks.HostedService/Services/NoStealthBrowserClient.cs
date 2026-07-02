namespace Equibles.CommonStocks.HostedService.Services;

/// <summary>
/// The standalone build's <see cref="IStealthBrowserClient"/>: no stealth engine is
/// bundled, so it reports <see cref="IsEnabled"/> false and every fetch degrades the
/// way a missing engine always has — callers fall back to their plain-HTTP paths.
/// A host that ships a real engine registers its own implementation ahead of this one.
/// </summary>
public class NoStealthBrowserClient : IStealthBrowserClient
{
    public bool IsEnabled => false;

    public Task<string> FetchHtml(string url, CancellationToken cancellationToken) =>
        Task.FromResult<string>(null);

    public Task<StealthFetchResult> TryFetchHtml(string url, CancellationToken cancellationToken) =>
        Task.FromResult(StealthFetchResult.Disabled);

    public Task<string> FetchRaw(string url, CancellationToken cancellationToken) =>
        Task.FromResult<string>(null);
}

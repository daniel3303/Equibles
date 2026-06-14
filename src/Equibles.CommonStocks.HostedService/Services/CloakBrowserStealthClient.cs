using Equibles.CommonStocks.HostedService.Configuration;
using Equibles.Core.AutoWiring;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;

namespace Equibles.CommonStocks.HostedService.Services;

/// <summary>
/// <see cref="IStealthBrowserClient"/> backed by a CloakBrowser <c>cloakserve</c>
/// sidecar. Connects to the sidecar's Chrome DevTools Protocol endpoint and renders
/// the page (or fetches a resource) through the stealth Chromium. The client is active
/// whenever a sidecar URL is configured; when none is set it reports
/// <see cref="IsEnabled"/> false and never runs, so a standalone build neither talks
/// to nor depends on the third-party binary (discovery falls back to plain HTTP).
/// </summary>
[Service(ServiceLifetime.Singleton, typeof(IStealthBrowserClient))]
public class CloakBrowserStealthClient : IStealthBrowserClient, IAsyncDisposable
{
    // Pulls a resource from within the cleared page context, returning the raw bytes
    // the server sent (e.g. an RSS feed) rather than the rendered DOM. Runs after the
    // page has navigated to the origin and cleared its bot challenge, so the request
    // rides the clearance cookie.
    private const string InPageFetchScript = """
        async (url) => {
            const response = await fetch(url, { credentials: 'include' });
            return response.ok ? await response.text() : null;
        }
        """;

    private readonly StealthFetchOptions _options;
    private readonly ILogger<CloakBrowserStealthClient> _logger;

    // Stealth renders are expensive and politeness-sensitive, so concurrency is
    // capped well below the plain-HTTP probe rate.
    private readonly SemaphoreSlim _concurrencyLimiter;

    // The Playwright driver is reused across fetches; a fresh browser connection and
    // context are taken per fetch so walled hosts never share cookie/fingerprint
    // state.
    private readonly SemaphoreSlim _driverLock = new(1, 1);
    private IPlaywright _playwright;

    public CloakBrowserStealthClient(
        IOptions<StealthFetchOptions> options,
        ILogger<CloakBrowserStealthClient> logger
    )
    {
        _options = options.Value;
        _logger = logger;
        _concurrencyLimiter = new SemaphoreSlim(Math.Max(1, _options.MaxConcurrency));
    }

    public bool IsEnabled => !string.IsNullOrWhiteSpace(_options.SidecarUrl);

    public Task<string> FetchHtml(string url, CancellationToken cancellationToken) =>
        RunInStealthPage(
            url,
            async page =>
            {
                await Navigate(page, url);
                return await page.ContentAsync();
            },
            cancellationToken
        );

    public Task<string> FetchRaw(string url, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return Task.FromResult<string>(null);

        return RunInStealthPage(
            url,
            async page =>
            {
                // Land on the origin first so the bot challenge clears and its
                // clearance cookie is set; the in-page fetch then rides that cleared
                // session and returns the raw document the parser needs.
                await Navigate(page, uri.GetLeftPart(UriPartial.Authority));
                return await page.EvaluateAsync<string>(InPageFetchScript, url);
            },
            cancellationToken
        );
    }

    /// <summary>
    /// Connects to the sidecar, runs <paramref name="action"/> against a fresh page
    /// in an isolated context, and disconnects. Returns null (degrading to a miss)
    /// when the engine is disabled or the connection/navigation/render fails.
    /// </summary>
    private async Task<string> RunInStealthPage(
        string url,
        Func<IPage, Task<string>> action,
        CancellationToken cancellationToken
    )
    {
        if (!IsEnabled)
            return null;

        await _concurrencyLimiter.WaitAsync(cancellationToken);
        try
        {
            var playwright = await EnsureDriver(cancellationToken);

            // cloakserve speaks CDP over WebSocket; connect, work, disconnect.
            await using var browser = await playwright.Chromium.ConnectOverCDPAsync(
                _options.SidecarUrl
            );
            var context = await browser.NewContextAsync();
            try
            {
                var page = await context.NewPageAsync();
                return await action(page);
            }
            finally
            {
                await context.CloseAsync();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (PlaywrightException ex)
        {
            // Connection refused, navigation failure, or render timeout. Enabled-but-
            // broken is a misconfiguration worth surfacing, but the caller still
            // degrades to a miss rather than failing the batch.
            _logger.LogWarning(ex, "Stealth fetch failed for {Url}", url);
            return null;
        }
    }

    private Task Navigate(IPage page, string url) =>
        page.GotoAsync(
            url,
            new PageGotoOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle,
                Timeout = _options.RenderTimeoutSeconds * 1000,
            }
        );

    private async Task<IPlaywright> EnsureDriver(CancellationToken cancellationToken)
    {
        if (_playwright != null)
            return _playwright;

        await _driverLock.WaitAsync(cancellationToken);
        try
        {
            _playwright ??= await Playwright.CreateAsync();
            return _playwright;
        }
        finally
        {
            _driverLock.Release();
        }
    }

    public ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        _playwright?.Dispose();
        _playwright = null;
        _concurrencyLimiter.Dispose();
        _driverLock.Dispose();
        return ValueTask.CompletedTask;
    }
}

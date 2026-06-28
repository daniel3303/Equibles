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

            // cloakserve speaks CDP over WebSocket; connect, work, disconnect. The connect is bounded
            // by a timeout: a wedged/over-loaded sidecar can leave ConnectOverCDPAsync hanging
            // indefinitely (no built-in timeout), and a single hung connect holds a concurrency slot
            // forever — enough of them stall the whole discovery sweep. On timeout this degrades to a
            // miss like any other connect failure, and the caller re-probes the stock later.
            await using var browser = await playwright
                .Chromium.ConnectOverCDPAsync(_options.SidecarUrl)
                .WaitAsync(TimeSpan.FromSeconds(_options.ConnectTimeoutSeconds), cancellationToken);
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
        catch (TimeoutException)
        {
            // The CDP connect outran ConnectTimeoutSeconds (wedged/over-loaded sidecar). Degrade to a
            // miss rather than holding the concurrency slot; the stock is re-probed on a later cycle.
            _logger.LogWarning("Stealth connect timed out for {Url}", url);
            return null;
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
        NavigateWaitingForNetworkIdle(page, url, _options.RenderTimeoutSeconds * 1000, _logger);

    // Brief settle after DOMContentLoaded to let client-rendered / post-bot-challenge content appear,
    // capped well below the full render budget. Waiting for full network-idle instead burned the
    // whole budget (~45s) on every IR/company page, because most stream background telemetry/ads that
    // never let the network fall idle — which made a full-universe discovery sweep infeasible.
    private const int SettleTimeoutMilliseconds = 8000;

    /// <summary>
    /// Navigates to <paramref name="url"/> waiting only for <see cref="WaitUntilState.DOMContentLoaded"/>
    /// — the document, not full network-idle — then gives the page a brief settle window for any
    /// client-rendered or post-challenge content to appear. Most IR/company pages stream background
    /// telemetry that never lets the network fall idle, so waiting for idle burned the entire render
    /// budget on every page; DOMContentLoaded plus a short settle captures the real content in
    /// seconds instead. A settle-wait timeout is non-fatal — the already-loaded DOM is kept. A genuine
    /// navigation failure (refused connection, DNS, protocol error) is a different exception that
    /// still propagates, so the fetch degrades to a miss as before. The settle timeout surfaces as a
    /// <see cref="System.TimeoutException"/> (or, on some paths, a message-matched
    /// <see cref="PlaywrightException"/>), both caught here.
    /// </summary>
    internal static async Task NavigateWaitingForNetworkIdle(
        IPage page,
        string url,
        int timeoutMilliseconds,
        ILogger logger
    )
    {
        await page.GotoAsync(
            url,
            new PageGotoOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded,
                Timeout = timeoutMilliseconds,
            }
        );

        try
        {
            await page.WaitForLoadStateAsync(
                LoadState.NetworkIdle,
                new PageWaitForLoadStateOptions { Timeout = SettleTimeoutMilliseconds }
            );
        }
        catch (System.TimeoutException)
        {
            logger.LogDebug("Settle wait timed out for {Url}; using the loaded DOM.", url);
        }
        catch (PlaywrightException ex) when (IsNetworkIdleTimeout(ex))
        {
            logger.LogDebug("Settle wait timed out for {Url}; using the loaded DOM.", url);
        }
    }

    /// <summary>
    /// A navigation that loads but never reaches network-idle throws a
    /// <see cref="PlaywrightException"/> whose message is the idle-wait timeout
    /// ("Timeout &lt;n&gt;ms exceeded"). This Playwright version has no dedicated timeout
    /// type, so the timeout is matched by message; every other navigation failure
    /// (refused connection, DNS, protocol error) does not match and still propagates.
    /// </summary>
    private static bool IsNetworkIdleTimeout(PlaywrightException ex) =>
        ex.Message.Contains("Timeout", StringComparison.OrdinalIgnoreCase);

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

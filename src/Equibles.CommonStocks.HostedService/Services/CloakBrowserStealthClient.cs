using Equibles.CommonStocks.HostedService.Configuration;
using Equibles.Core.AutoWiring;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;

namespace Equibles.CommonStocks.HostedService.Services;

/// <summary>
/// <see cref="IStealthBrowserClient"/> backed by a CloakBrowser <c>cloakserve</c>
/// sidecar. Connects to the sidecar's Chrome DevTools Protocol endpoint, renders
/// the page in the stealth Chromium, and returns the final HTML. The sidecar is an
/// opt-in, digest-pinned container (compose <c>--profile stealth</c>); when it is
/// not configured this client reports <see cref="IsEnabled"/> false and never runs,
/// so the default build neither talks to nor depends on the third-party binary.
/// </summary>
[Service(ServiceLifetime.Singleton, typeof(IStealthBrowserClient))]
public class CloakBrowserStealthClient : IStealthBrowserClient, IAsyncDisposable
{
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

    public bool IsEnabled => _options.Enabled && !string.IsNullOrWhiteSpace(_options.SidecarUrl);

    public async Task<string> FetchHtml(string url, CancellationToken cancellationToken)
    {
        if (!IsEnabled)
            return null;

        await _concurrencyLimiter.WaitAsync(cancellationToken);
        try
        {
            var playwright = await EnsureDriver(cancellationToken);

            // cloakserve speaks CDP over WebSocket; connect, render, disconnect.
            await using var browser = await playwright.Chromium.ConnectOverCDPAsync(
                _options.SidecarUrl
            );
            var context = await browser.NewContextAsync();
            try
            {
                var page = await context.NewPageAsync();
                await page.GotoAsync(
                    url,
                    new PageGotoOptions
                    {
                        WaitUntil = WaitUntilState.NetworkIdle,
                        Timeout = _options.RenderTimeoutSeconds * 1000,
                    }
                );
                return await page.ContentAsync();
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

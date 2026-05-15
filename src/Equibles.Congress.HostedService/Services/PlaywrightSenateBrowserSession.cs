using System.Text.Json;
using Equibles.Core.AutoWiring;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;

namespace Equibles.Congress.HostedService.Services;

/// <summary>
/// Playwright/Firefox implementation of <see cref="ISenateBrowserSession"/>.
/// This is the only Senate code that touches a real browser; the testable
/// retry/pagination/parsing logic lives in <see cref="SenateDisclosureClient"/>.
/// </summary>
[Service(ServiceLifetime.Scoped, typeof(ISenateBrowserSession))]
public class PlaywrightSenateBrowserSession : ISenateBrowserSession
{
    private const string BaseUrl = "https://efdsearch.senate.gov";
    private const string HomeUrl = BaseUrl + "/search/home/";
    private const int BrowserFetchTimeoutMs = 30_000;

    private readonly ILogger<PlaywrightSenateBrowserSession> _logger;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    private IPlaywright _playwright;
    private IBrowser _browser;
    private IPage _page;
    private bool _authenticated;

    // JavaScript executed in the browser context to make HTTP requests.
    // Reuses the browser's TLS fingerprint and session cookies to bypass Akamai bot detection.
    // For POST requests, extracts the CSRF token from cookies and includes it in headers and form data.
    private const string BrowserFetchScript = """
        async ({url, formFields}) => {
            const controller = new AbortController();
            const timeoutId = setTimeout(() => controller.abort(), 30000);
            try {
                const options = { signal: controller.signal };
                if (formFields) {
                    const csrfToken = document.cookie.split(';')
                        .map(c => c.trim())
                        .find(c => c.startsWith('csrftoken='))
                        ?.split('=')[1] ?? '';
                    formFields['csrftoken'] = csrfToken;
                    options.method = 'POST';
                    options.headers = {
                        'Content-Type': 'application/x-www-form-urlencoded',
                        'X-CSRFToken': csrfToken,
                        'Referer': location.origin + '/search/',
                    };
                    options.body = new URLSearchParams(formFields).toString();
                }
                const resp = await fetch(url, options);
                return { status: resp.status, body: await resp.text() };
            } finally {
                clearTimeout(timeoutId);
            }
        }
        """;

    public PlaywrightSenateBrowserSession(ILogger<PlaywrightSenateBrowserSession> logger)
    {
        _logger = logger;
    }

    public async Task EnsureAuthenticated(CancellationToken ct)
    {
        if (_authenticated)
            return;

        await _initLock.WaitAsync(ct);
        try
        {
            if (_authenticated)
                return;

            _playwright = await Playwright.CreateAsync();
            _browser = await _playwright.Firefox.LaunchAsync(
                new BrowserTypeLaunchOptions { Headless = true }
            );

            var context = await _browser.NewContextAsync();
            context.SetDefaultTimeout(BrowserFetchTimeoutMs);
            _page = await context.NewPageAsync();

            _logger.LogDebug("Navigating to Senate eFD via Playwright Firefox");

            var response = await _page.GotoAsync(
                HomeUrl,
                new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 30_000 }
            );

            if (response?.Status != 200)
                throw new HttpRequestException(
                    $"Senate eFD home page returned HTTP {response?.Status}"
                );

            // Accept the prohibition agreement disclaimer
            var checkbox = _page.Locator("#agree_statement");
            if (await checkbox.IsVisibleAsync())
            {
                await checkbox.CheckAsync();
                await _page.Locator("button[type='submit'], input[type='submit']").ClickAsync();
                await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);
            }

            _authenticated = true;
            _logger.LogDebug("Senate eFD disclaimer accepted via Playwright Firefox");
        }
        finally
        {
            _initLock.Release();
        }
    }

    public async Task<SenateFetchResult> Fetch(
        string url,
        Dictionary<string, string> formFields,
        CancellationToken ct
    )
    {
        JsonElement result;
        try
        {
            result = await _page.EvaluateAsync<JsonElement>(
                BrowserFetchScript,
                new { url, formFields }
            );
        }
        catch (PlaywrightException) when (ct.IsCancellationRequested)
        {
            throw new OperationCanceledException(ct);
        }
        catch (PlaywrightException ex)
        {
            throw new SenateBrowserException($"Browser fetch failed for {url}", ex);
        }

        return new SenateFetchResult
        {
            Status = result.GetProperty("status").GetInt32(),
            Body = result.GetProperty("body").GetString(),
        };
    }

    public async ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);

        try
        {
            if (_page != null)
            {
                await _page.Context.CloseAsync();
                _page = null;
            }
        }
        catch (PlaywrightException)
        {
            _page = null;
        }

        try
        {
            if (_browser != null)
            {
                await _browser.CloseAsync();
                _browser = null;
            }
        }
        catch (PlaywrightException)
        {
            _browser = null;
        }

        _playwright?.Dispose();
        _playwright = null;
        _initLock.Dispose();
    }
}

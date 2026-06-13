namespace Equibles.CommonStocks.HostedService.Services;

/// <summary>
/// Renders a URL through a stealth browser and returns the final HTML. Used as a
/// fallback when a plain <see cref="HttpClient"/> fetch is answered by a bot-
/// protection challenge (Imperva Incapsula, Akamai) instead of the real page. The
/// backing engine — a containerised stealth Chromium, a hosted unblocker, or any
/// future replacement — sits behind this interface so the discovery and feed code
/// is never hard-coupled to one binary.
/// </summary>
public interface IStealthBrowserClient
{
    /// <summary>
    /// True when a stealth engine is configured and enabled. Callers check this
    /// before attempting a stealth re-fetch, so behaviour is unchanged when the
    /// sidecar is absent (the default).
    /// </summary>
    bool IsEnabled { get; }

    /// <summary>
    /// Navigates to <paramref name="url"/> through the stealth browser and returns
    /// the rendered HTML, or null when the engine is disabled or the page could not
    /// be rendered. A render failure degrades to null rather than throwing so a
    /// single walled host never fails the whole discovery batch.
    /// </summary>
    Task<string> FetchHtml(string url, CancellationToken cancellationToken);
}

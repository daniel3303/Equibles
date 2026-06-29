namespace Equibles.CommonStocks.HostedService.Services;

/// <summary>
/// Renders a URL through a stealth browser and returns the final HTML. This is the
/// primary fetch path for company websites and IR pages — most are bot-protected
/// (Imperva Incapsula, Akamai, Cloudflare), so a plain <see cref="HttpClient"/> would
/// just be walled. The backing engine — a containerised stealth Chromium, a hosted
/// unblocker, or any future replacement — sits behind this interface so the discovery
/// and feed code is never hard-coupled to one binary.
/// </summary>
public interface IStealthBrowserClient
{
    /// <summary>
    /// True when a stealth engine is configured (a sidecar URL is set). Callers route
    /// company/IR fetches through the sidecar when this is true and fall back to plain
    /// HTTP when it is false (the default for a standalone build with no sidecar).
    /// </summary>
    bool IsEnabled { get; }

    /// <summary>
    /// Navigates to <paramref name="url"/> through the stealth browser and returns
    /// the rendered HTML, or null when the engine is disabled or the page could not
    /// be rendered. A render failure degrades to null rather than throwing so a
    /// single walled host never fails the whole discovery batch.
    /// </summary>
    Task<string> FetchHtml(string url, CancellationToken cancellationToken);

    /// <summary>
    /// Like <see cref="FetchHtml"/>, but returns a <see cref="StealthFetchResult"/> that classifies the
    /// outcome — rendered, page-unavailable (conclusive), or sidecar-unavailable (transient). Callers
    /// that must distinguish "the page genuinely isn't there" from "the engine couldn't reach it this
    /// time" use this; everyone else can keep using <see cref="FetchHtml"/>. Degrades rather than
    /// throwing, on the same contract as <see cref="FetchHtml"/>.
    /// </summary>
    Task<StealthFetchResult> TryFetchHtml(string url, CancellationToken cancellationToken);

    /// <summary>
    /// Clears the host's bot challenge in the stealth browser, then fetches
    /// <paramref name="url"/> from within that cleared page context and returns the
    /// raw response body. Unlike <see cref="FetchHtml"/> this returns the bytes the
    /// server sent rather than the rendered DOM, so a feed (RSS/XML) parser sees the
    /// real document. Returns null when the engine is disabled or the fetch fails.
    /// </summary>
    Task<string> FetchRaw(string url, CancellationToken cancellationToken);
}

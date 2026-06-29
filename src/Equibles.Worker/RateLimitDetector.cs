namespace Equibles.Worker;

/// <summary>
/// Recognises a rate-limit response from a host, whether seen as an HTTP status (plain-HTTP probes) or
/// as the rendered interstitial a stealth browser lands on (browser renders never surface a status).
/// Deliberately narrow — only the unambiguous Cloudflare 1015 / HTTP 429 signatures — so a page that
/// merely mentions rate limiting in its copy isn't mistaken for a block.
/// </summary>
public static class RateLimitDetector
{
    public static bool IsRateLimited(int? statusCode, string html)
    {
        if (statusCode == 429)
            return true;

        if (string.IsNullOrEmpty(html))
            return false;

        // Cloudflare's 1015 interstitial. Both phrases appear on that page and nowhere benign.
        return html.Contains("Error 1015", StringComparison.OrdinalIgnoreCase)
            || html.Contains("You are being rate limited", StringComparison.OrdinalIgnoreCase);
    }
}

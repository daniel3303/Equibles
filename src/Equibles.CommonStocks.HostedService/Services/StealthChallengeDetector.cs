namespace Equibles.CommonStocks.HostedService.Services;

/// <summary>
/// Recognises the bot-protection challenge stubs that Imperva Incapsula and Akamai
/// return in place of a real page. A challenge response is a small HTML (or text)
/// body carrying vendor-specific markers and none of the page's actual content, so
/// a plain fetch finds no IR keywords and records a false miss. Detecting it lets
/// the caller re-fetch through the stealth-browser path. Pure logic (no I/O) so it
/// is unit-testable against fixture bodies.
/// </summary>
public static class StealthChallengeDetector
{
    // Markers that appear only in a challenge / interstitial / hard-block response,
    // never in a genuine IR page. Kept deliberately specific: the detector decides
    // whether a non-validating response is worth an (expensive, rate-limited)
    // stealth re-fetch, so a broad marker like a bare "akamai" CDN reference — which
    // legitimate pages carry — would route the whole universe through the sidecar.
    // Matched case-insensitively against the raw response body.
    private static readonly string[] ChallengeMarkers =
    [
        "_incapsula_resource", // Incapsula challenge bootstrap script
        "incapsula incident id", // Incapsula block page
        "powered by incapsula", // Incapsula interstitial footer
        "errors.edgesuite.net", // Akamai error/interstitial asset host
        "you don't have permission to access", // Akamai "Access Denied" body
        "access denied", // Akamai / generic WAF block title
        "code 1011", // Provider hard lock seen on walled IR and webcast hosts
        "pardon the interruption", // Imperva interstitial copy
    ];

    /// <summary>
    /// True when <paramref name="body"/> looks like a bot-protection challenge stub
    /// rather than a real page.
    /// </summary>
    public static bool IsChallenge(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return false;

        var haystack = body.ToLowerInvariant();
        return ChallengeMarkers.Any(haystack.Contains);
    }
}

namespace Equibles.CommonStocks.BusinessLogic.Websites;

/// <summary>
/// Refuses shared platforms (social networks, marketplaces, app stores, regulators) as an issuer's own
/// website: a filer's LinkedIn or Amazon storefront is reachable, but every issuer on that host would
/// then share one investor-relations site.
/// </summary>
public static class IssuerWebsitePolicy
{
    private static readonly string[] SharedPlatformHosts =
    [
        "linkedin.com",
        "facebook.com",
        "instagram.com",
        "x.com",
        "twitter.com",
        "youtube.com",
        "tiktok.com",
        "reddit.com",
        "amazon.com",
        "apple.com",
        "google.com",
        "play.google.com",
        "wikipedia.org",
        "sec.gov",
        "crunchbase.com",
        "glassdoor.com",
        "bloomberg.com",
    ];

    public static bool IsSharedPlatform(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;
        var candidate = url.Trim();
        if (!candidate.Contains("://"))
            candidate = "https://" + candidate;
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Host))
            return false;
        var host = uri.Host.ToLowerInvariant();
        return SharedPlatformHosts.Any(platform =>
            host == platform || host.EndsWith("." + platform, StringComparison.Ordinal)
        );
    }
}

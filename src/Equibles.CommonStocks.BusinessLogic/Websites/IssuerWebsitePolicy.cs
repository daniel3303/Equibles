namespace Equibles.CommonStocks.BusinessLogic.Websites;

/// <summary>
/// Refuses shared platforms (social networks, encyclopedias, regulators, data aggregators) as an
/// issuer's own website: a filer's LinkedIn page is reachable, but every issuer on that host would
/// then share one investor-relations site. Hosts a listed issuer runs as its own site (apple.com,
/// amazon.com, facebook.com, reddit.com) are deliberately absent.
/// </summary>
public static class IssuerWebsitePolicy
{
    private static readonly string[] SharedPlatformHosts =
    [
        "linkedin.com",
        "x.com",
        "twitter.com",
        "youtube.com",
        "tiktok.com",
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
        if (
            !Uri.TryCreate(candidate, UriKind.Absolute, out var uri)
            || string.IsNullOrEmpty(uri.Host)
        )
            return false;
        var host = uri.Host.TrimEnd('.').ToLowerInvariant();
        return SharedPlatformHosts.Any(platform =>
            host == platform || host.EndsWith("." + platform, StringComparison.Ordinal)
        );
    }
}

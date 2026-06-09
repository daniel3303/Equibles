namespace Equibles.CommonStocks.HostedService.Services;

/// <summary>
/// Builds the ordered list of candidate investor-relations URLs to probe for a
/// company, derived from its website. Pure logic (no I/O) so it is unit-testable
/// in isolation from the network probe.
/// </summary>
public static class InvestorRelationsCandidateBuilder
{
    /// <summary>
    /// Produces candidate URLs for <paramref name="website"/>: first the company
    /// website with each <paramref name="paths"/> segment appended, then each
    /// <paramref name="subdomains"/> prefix in front of the registrable domain.
    /// Returns an empty list when the website is missing or unparseable. Results
    /// are de-duplicated case-insensitively while preserving order.
    /// </summary>
    public static IReadOnlyList<string> Build(
        string website,
        IEnumerable<string> paths,
        IEnumerable<string> subdomains
    )
    {
        if (string.IsNullOrWhiteSpace(website))
            return [];

        // EDGAR-sourced websites occasionally omit the scheme ("acme.com"); assume
        // https so Uri can parse them as absolute.
        var normalized = website.Trim();
        if (!normalized.Contains("://"))
            normalized = "https://" + normalized;

        if (
            !Uri.TryCreate(normalized, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || string.IsNullOrEmpty(uri.Host)
        )
            return [];

        var scheme = uri.Scheme;
        var host = uri.Host.ToLowerInvariant();

        // Strip a leading "www." so subdomain candidates attach to the registrable
        // domain (ir.acme.com, not ir.www.acme.com).
        var registrableDomain = host.StartsWith("www.") ? host[4..] : host;

        var candidates = new List<string>();

        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path))
                continue;
            candidates.Add($"{scheme}://{host}/{path.Trim('/')}");
        }

        foreach (var subdomain in subdomains)
        {
            if (string.IsNullOrWhiteSpace(subdomain))
                continue;
            candidates.Add($"{scheme}://{subdomain.Trim('.')}.{registrableDomain}");
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return candidates.Where(seen.Add).ToList();
    }
}

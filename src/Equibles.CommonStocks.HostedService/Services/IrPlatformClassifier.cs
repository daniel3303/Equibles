using Equibles.CommonStocks.Data.Models;

namespace Equibles.CommonStocks.HostedService.Services;

/// <summary>
/// Classifies which platform a company's investor-relations website runs on by
/// looking for deterministic vendor markers (CDN / asset domains) in the page
/// HTML. Pure logic (no I/O) so it is unit-testable against fixture HTML.
/// </summary>
public static class IrPlatformClassifier
{
    // Ordered vendor signatures: the first platform whose any marker appears in the
    // page HTML wins. Markers are vendor-owned domains a hosted IR site loads assets
    // from, so they don't collide with ordinary page content. Extend as more
    // platforms are confirmed.
    private static readonly (IrPlatformType Platform, string[] Markers)[] Signatures =
    [
        (IrPlatformType.Q4Inc, ["q4cdn.com", "q4inc.com", "q4web.com"]),
        (IrPlatformType.BusinessWire, ["businesswire.com"]),
        (IrPlatformType.Notified, ["notified.com", "globenewswire.com", "globenewswire"]),
        (IrPlatformType.NasdaqIrInsight, ["ir.nasdaq.com", "nasdaqomx", "irinsight"]),
    ];

    /// <summary>
    /// Returns the platform detected from <paramref name="html"/>:
    /// <see cref="IrPlatformType.Unknown"/> when there is no HTML to inspect, a
    /// specific vendor when one of its markers is present, or
    /// <see cref="IrPlatformType.Custom"/> for a real IR page matching no known
    /// vendor (a bespoke site).
    /// </summary>
    public static IrPlatformType Classify(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return IrPlatformType.Unknown;

        var haystack = html.ToLowerInvariant();

        foreach (var (platform, markers) in Signatures)
        {
            if (markers.Any(haystack.Contains))
                return platform;
        }

        return IrPlatformType.Custom;
    }
}

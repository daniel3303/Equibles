using Microsoft.Extensions.Configuration;

namespace Equibles.Sec.HostedService.Helpers;

/// <summary>
/// The SEC fund series whose NPORT-P schedule of investments is stored whole, opting them out of
/// the daily-index sweep's narrowing to positions in tracked stocks.
///
/// The sweep exists to answer the reverse "who holds this stock" lookup, so it keeps only the rows
/// whose CUSIP matches a stock we track and drops the rest of the portfolio before persisting.
/// That narrowing is lossy and unrecoverable by query — the dropped rows are never written — so a
/// consumer that needs a fund's complete schedule (reading an index tracker's portfolio as the
/// index's constituent list, say) cannot be served from a narrowed filing at all. Listing a series
/// here trades the storage of its whole portfolio for a complete schedule.
///
/// Empty by default: with nothing configured the sweep narrows exactly as it always has.
/// </summary>
public static class NportFullFidelitySeries
{
    /// <summary>Configuration key holding the opted-in SEC series identifiers.</summary>
    public const string ConfigurationKey = "NportSweep:FullFidelitySeriesIds";

    /// <summary>No series opted in — the default, and the sweep's historical behaviour.</summary>
    public static readonly IReadOnlySet<string> None = new HashSet<string>(
        StringComparer.OrdinalIgnoreCase
    );

    private static readonly char[] Separators = [',', ';', ' ', '\t', '\r', '\n'];

    /// <summary>
    /// Reads the opted-in series identifiers. Accepts either a configuration array (an appsettings
    /// list, or indexed <c>NportSweep__FullFidelitySeriesIds__0</c> environment variables) or one
    /// delimited scalar (<c>NportSweep__FullFidelitySeriesIds=S000030000,S000030003</c>) — the
    /// shape a container deployment can express in a single variable. Blank entries are dropped and
    /// matching is case-insensitive, so a hand-typed value still lines up with EDGAR's uppercase
    /// identifiers.
    /// </summary>
    public static IReadOnlySet<string> FromConfiguration(IConfiguration configuration)
    {
        var section = configuration?.GetSection(ConfigurationKey);
        if (section == null)
            return None;

        var seriesIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // The scalar form carries every id in one delimited value; the array form carries one per
        // child. Both are read so a mixed configuration (an appsettings array overridden by a
        // single environment variable) still resolves to the union rather than silently to one.
        AddDelimited(seriesIds, section.Value);
        foreach (var child in section.GetChildren())
            AddDelimited(seriesIds, child.Value);

        return seriesIds;
    }

    private static void AddDelimited(HashSet<string> seriesIds, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        foreach (var seriesId in value.Split(Separators, StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = seriesId.Trim();
            if (trimmed.Length > 0)
                seriesIds.Add(trimmed);
        }
    }
}

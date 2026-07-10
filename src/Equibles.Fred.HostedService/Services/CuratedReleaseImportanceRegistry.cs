using Equibles.Fred.Data.Models;

namespace Equibles.Fred.HostedService.Services;

/// <summary>
/// Curated importance tier per FRED release, keyed by FRED's numeric release id. The
/// calendar importer stamps these onto <see cref="FredRelease"/> every cycle, so editing
/// this map heals already-stored releases. Releases without an entry resolve to Low —
/// the right default for the daily rate/market-level prints that dominate the untiered
/// feed. When adding a series to <see cref="CuratedSeriesRegistry"/> whose release is a
/// scheduled announcement rather than a daily level, add its release here too.
/// </summary>
public static class CuratedReleaseImportanceRegistry
{
    private static readonly IReadOnlyDictionary<int, FredReleaseImportance> Importance =
        new Dictionary<int, FredReleaseImportance>
        {
            // High — the tier-1 scheduled market movers.
            [9] = FredReleaseImportance.High, // Advance Monthly Sales for Retail and Food Services
            [10] = FredReleaseImportance.High, // Consumer Price Index
            [46] = FredReleaseImportance.High, // Producer Price Index
            [50] = FredReleaseImportance.High, // Employment Situation
            [53] = FredReleaseImportance.High, // Gross Domestic Product
            [54] = FredReleaseImportance.High, // Personal Income and Outlays (PCE)
            [101] = FredReleaseImportance.High, // FOMC Press Release
            // Medium — genuine scheduled prints that inform but rarely move markets alone.
            [13] = FredReleaseImportance.Medium, // G.17 Industrial Production and Capacity Utilization
            [20] = FredReleaseImportance.Medium, // H.4.1 Factors Affecting Reserve Balances
            [21] = FredReleaseImportance.Medium, // H.6 Money Stock Measures
            [27] = FredReleaseImportance.Medium, // New Residential Construction
            [91] = FredReleaseImportance.Medium, // Surveys of Consumers
            [180] = FredReleaseImportance.Medium, // Unemployment Insurance Weekly Claims Report
            [190] = FredReleaseImportance.Medium, // Primary Mortgage Market Survey
            [192] = FredReleaseImportance.Medium, // Job Openings and Labor Turnover Survey
            [199] = FredReleaseImportance.Medium, // S&P Case-Shiller Home Price Indices
            // Everything else — daily rate/market levels (H.15, SOFR, EFFR, VIX, bond
            // yields, FX, stress indices) — stays Low via the fallback.
        };

    public static FredReleaseImportance Resolve(int releaseId) =>
        Importance.TryGetValue(releaseId, out var importance)
            ? importance
            : FredReleaseImportance.Low;
}

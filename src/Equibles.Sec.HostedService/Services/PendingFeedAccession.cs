namespace Equibles.Sec.HostedService.Services;

/// <summary>
/// Cross-cycle memo for a filing the realtime feed flagged but the company's
/// submissions enumeration hasn't confirmed yet — the submissions JSON lags
/// acceptance, so the first enumeration after a flag can legitimately miss the
/// filing (see <see cref="FilingDiscoveryService"/>).
/// </summary>
internal sealed class PendingFeedAccession
{
    public long Cik { get; set; }

    public DateTime FirstSeenAtUtc { get; set; }

    public DateTime LastRetriedAtUtc { get; set; }

    /// <summary>How many times the company was re-dirtied for this filing.</summary>
    public int RetryCount { get; set; }

    /// <summary>Whether the one-time "not yet enumerable" notice fired.</summary>
    public bool RetryLogged { get; set; }
}

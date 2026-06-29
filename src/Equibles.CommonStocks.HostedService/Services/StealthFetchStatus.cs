namespace Equibles.CommonStocks.HostedService.Services;

/// <summary>
/// Why a stealth fetch produced (or failed to produce) HTML. Lets a caller tell a
/// <em>conclusive</em> answer about the page apart from a <em>transient</em> infrastructure
/// failure, so a momentarily-unavailable sidecar is not mistaken for "the page isn't there".
/// </summary>
public enum StealthFetchStatus
{
    /// <summary>The page navigated and returned HTML — the caller inspects the content to decide.</summary>
    Rendered,

    /// <summary>
    /// The page is definitively not there: DNS did not resolve, or the address was unreachable/invalid.
    /// Conclusive — re-probing won't change the answer.
    /// </summary>
    PageUnavailable,

    /// <summary>
    /// The stealth engine could not complete the render: connect/operation timeout, render timeout, or a
    /// reaped/wedged sidecar. Transient — the page may well render on a later attempt.
    /// </summary>
    SidecarUnavailable,

    /// <summary>No stealth engine is configured, so nothing was fetched.</summary>
    Disabled,
}

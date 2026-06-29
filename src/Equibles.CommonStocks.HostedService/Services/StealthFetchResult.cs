namespace Equibles.CommonStocks.HostedService.Services;

/// <summary>
/// Outcome of a stealth fetch: the <see cref="Status"/> classifying what happened and, when the page
/// rendered, its <see cref="Html"/> (null otherwise).
/// </summary>
public sealed record StealthFetchResult(StealthFetchStatus Status, string Html)
{
    public static StealthFetchResult Rendered(string html) =>
        new(StealthFetchStatus.Rendered, html);

    public static readonly StealthFetchResult PageUnavailable = new(
        StealthFetchStatus.PageUnavailable,
        null
    );

    public static readonly StealthFetchResult SidecarUnavailable = new(
        StealthFetchStatus.SidecarUnavailable,
        null
    );

    public static readonly StealthFetchResult Disabled = new(StealthFetchStatus.Disabled, null);
}

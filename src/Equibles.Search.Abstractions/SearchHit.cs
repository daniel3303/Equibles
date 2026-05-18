namespace Equibles.Search.Abstractions;

/// <summary>
/// One result. Carries only primitives — never an MVC URL — so providers stay framework-agnostic.
/// The Web layer maps <see cref="Kind"/> + <see cref="RouteValues"/> to a route via Url.Action.
/// </summary>
public class SearchHit
{
    public string Title { get; set; }

    public string Subtitle { get; set; }

    /// <summary>Stable discriminator the consumer maps to a destination, e.g. "Stock", "Filing".</summary>
    public string Kind { get; set; }

    /// <summary>Provider-local relevance; only meaningful for ranking within the same group.</summary>
    public double Score { get; set; }

    /// <summary>Route components the consumer needs to build a link (e.g. ticker, id, seriesId).</summary>
    public Dictionary<string, string> RouteValues { get; set; } = [];
}
